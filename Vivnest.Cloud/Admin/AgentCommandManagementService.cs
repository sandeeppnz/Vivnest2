using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Admin;

public sealed class AgentCommandManagementService : IAgentCommandManagementService
{
    private readonly IAgentCommandStore _commands;

    public AgentCommandManagementService(IAgentCommandStore commands)
    {
        _commands = commands;
    }

    public async Task<IReadOnlyList<AgentCommandDto>> GetByAgentAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _commands.GetByAgentAsync(tenantId, siteId, agentId, cancellationToken);

        return entities
            .OrderByDescending(e => e.CreatedUtc)
            .Select(CommandDispatcher.ToDto)
            .ToList();
    }

    public async Task<AgentCommandDto?> GetAsync(
        string tenantId,
        string siteId,
        string commandId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _commands.GetAsync(tenantId, siteId, commandId, cancellationToken);

        return entity == null ? null : CommandDispatcher.ToDto(entity);
    }

    public async Task<AgentCommandDto?> UpdateStatusAsync(
        string tenantId,
        string siteId,
        string commandId,
        AgentCommandStatus status,
        string? result,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken = default)
    {
        var entity = await _commands.GetAsync(tenantId, siteId, commandId, cancellationToken);

        if (entity == null)
            return null;

        var current = Enum.Parse<AgentCommandStatus>(entity.Status);

        // Already resolved - a duplicate/late callback (at-least-once
        // queue delivery, or a race between the Agent's own report and
        // this service's heartbeat-correlation hook) is a no-op that
        // returns the existing result rather than re-applying it.
        if (IsTerminal(current))
            return CommandDispatcher.ToDto(entity);

        var command = ToDomain(entity);

        switch (status)
        {
            case AgentCommandStatus.Received:
                command.MarkReceived();
                break;
            case AgentCommandStatus.Executing:
                command.MarkExecuting();
                break;
            case AgentCommandStatus.Succeeded:
                command.MarkSucceeded(result);
                break;
            case AgentCommandStatus.Failed:
                command.MarkFailed(errorCode ?? "UNKNOWN", errorMessage ?? "Unknown error.");
                break;
            default:
                // Pending/Dispatched/Expired/Cancelled aren't settable
                // through this Agent-facing callback.
                return CommandDispatcher.ToDto(entity);
        }

        var updated = ToEntity(command);
        updated.ETag = entity.ETag;

        await _commands.UpdateAsync(updated, cancellationToken);

        return CommandDispatcher.ToDto(updated);
    }

    public async Task EvaluateAgentCommandsAsync(
        AgentHeartbeatEntity agent,
        CancellationToken cancellationToken = default)
    {
        var entities = await _commands.GetByAgentAsync(
            agent.TenantId, agent.SiteId, agent.RowKey, cancellationToken);

        foreach (var entity in entities)
        {
            // Decision-log.md ADR-079 - RestartAgent only this pass
            // (Phase 9 Pass 1). Pass 2 extends this with
            // RefreshConfiguration/ApplyConfiguration's extra "does the
            // reported config now match what was expected" check; Pass 3
            // with ExecuteCapability's opposite rule (a fresh restart
            // while that command is still in flight means an unexpected
            // crash, not success).
            if (!string.Equals(entity.CommandType, AgentCommandTypes.RestartAgent, StringComparison.Ordinal))
                continue;

            var status = Enum.Parse<AgentCommandStatus>(entity.Status);

            if (status != AgentCommandStatus.Dispatched && status != AgentCommandStatus.Received)
                continue;

            if (entity.DispatchedUtc is not { } dispatchedUtc || agent.StartedUtc <= dispatchedUtc)
                continue;

            var command = ToDomain(entity);
            command.MarkSucceeded(null);

            var updated = ToEntity(command);
            updated.ETag = entity.ETag;

            await _commands.UpdateAsync(updated, cancellationToken);
        }
    }

    private static bool IsTerminal(AgentCommandStatus status) =>
        status is AgentCommandStatus.Succeeded
            or AgentCommandStatus.Failed
            or AgentCommandStatus.Expired
            or AgentCommandStatus.Cancelled;

    private static AgentCommand ToDomain(AgentCommandEntity entity)
    {
        return AgentCommand.Rehydrate(
            entity.TenantId,
            entity.SiteId,
            entity.RowKey,
            entity.AgentId,
            entity.TargetDeviceId,
            entity.CapabilityId,
            entity.CommandType,
            Enum.Parse<AgentCommandStatus>(entity.Status),
            entity.Payload,
            entity.Result,
            entity.ErrorCode,
            entity.ErrorMessage,
            entity.RequestedBy,
            entity.CreatedUtc,
            entity.DispatchedUtc,
            entity.ReceivedUtc,
            entity.StartedUtc,
            entity.CompletedUtc,
            entity.ExpiresUtc);
    }

    private static AgentCommandEntity ToEntity(AgentCommand command)
    {
        return new AgentCommandEntity
        {
            PartitionKey = new SiteScope(command.TenantId, command.SiteId).PartitionKey,
            RowKey = command.CommandId,
            TenantId = command.TenantId,
            SiteId = command.SiteId,
            AgentId = command.TargetAgentId,
            TargetDeviceId = command.TargetDeviceId,
            CapabilityId = command.CapabilityId,
            CommandType = command.CommandType,
            Status = command.Status.ToString(),
            Payload = command.Payload,
            Result = command.Result,
            ErrorCode = command.ErrorCode,
            ErrorMessage = command.ErrorMessage,
            RequestedBy = command.RequestedBy,
            CreatedUtc = command.CreatedUtc,
            DispatchedUtc = command.DispatchedUtc,
            ReceivedUtc = command.ReceivedUtc,
            StartedUtc = command.StartedUtc,
            CompletedUtc = command.CompletedUtc,
            ExpiresUtc = command.ExpiresUtc
        };
    }
}
