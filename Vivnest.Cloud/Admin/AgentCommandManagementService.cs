using System.Text.Json;
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
        if (current.IsTerminal())
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
            var isRestart = string.Equals(entity.CommandType, AgentCommandTypes.RestartAgent, StringComparison.Ordinal);

            // Decision-log.md ADR-080 - RefreshConfiguration/ApplyConfiguration
            // share RestartAgent's "confirmed by the next heartbeat"
            // mechanism, with one extra requirement below.
            var isConfigCommand =
                string.Equals(entity.CommandType, AgentCommandTypes.RefreshConfiguration, StringComparison.Ordinal) ||
                string.Equals(entity.CommandType, AgentCommandTypes.ApplyConfiguration, StringComparison.Ordinal);

            // Decision-log.md ADR-081 - the opposite rule from the two
            // above: ExecuteCapability never causes a restart on its own
            // success path (it self-reports Succeeded/Failed directly,
            // never leaving the process running long enough to reach here
            // in the success case). If a heartbeat with a newer StartedUtc
            // ever DOES show up while one is still Received/Executing,
            // that's proof of an unexpected crash mid-command, not a sign
            // of completion - implements the spec's own "Agent restart
            // during command execution does not falsely mark the command
            // successful" acceptance test.
            var isExecuteCapability =
                string.Equals(entity.CommandType, AgentCommandTypes.ExecuteCapability, StringComparison.Ordinal);

            if (!isRestart && !isConfigCommand && !isExecuteCapability)
                continue;

            var status = Enum.Parse<AgentCommandStatus>(entity.Status);

            // Decision-log.md ADR-080 - a real bug, found live: RestartAgent
            // never reports Executing (Received -> the process just dies),
            // but RefreshConfiguration/ApplyConfiguration's Agent-side
            // handler explicitly reports Executing before restarting - by
            // the time the post-restart heartbeat arrives, the command is
            // already sitting in Executing, not Dispatched/Received, so the
            // original Pass 1 filter (copied verbatim) silently never
            // matched it. Confirmed live: the command stayed stuck at
            // Executing forever despite a correctly-reported matching
            // ConfigurationVersion, until this filter was widened.
            if (status != AgentCommandStatus.Dispatched &&
                status != AgentCommandStatus.Received &&
                status != AgentCommandStatus.Executing)
                continue;

            if (entity.DispatchedUtc is not { } dispatchedUtc || agent.StartedUtc <= dispatchedUtc)
                continue;

            if (isExecuteCapability)
            {
                var crashed = ToDomain(entity);
                crashed.MarkFailed(
                    "AGENT_RESTARTED",
                    "The Agent restarted unexpectedly while this command was still in flight.");

                var updatedCrashed = ToEntity(crashed);
                updatedCrashed.ETag = entity.ETag;

                await _commands.UpdateAsync(updatedCrashed, cancellationToken);

                continue;
            }

            if (isConfigCommand)
            {
                // Decision-log.md ADR-080 - a fresh restart alone isn't
                // proof the NEW configuration is what's actually
                // running (the process could have restarted for an
                // unrelated reason right after this command was
                // dispatched) - also require the heartbeat's own
                // reported ConfigurationVersion to match what this
                // command targeted. If it doesn't match yet, wait for a
                // later heartbeat rather than failing - there's no way
                // to distinguish "hasn't picked up the new config yet"
                // from "never will" until the command expires.
                var targetVersion = ParseTargetVersion(entity.Payload);

                if (targetVersion is null || agent.ConfigurationVersion != targetVersion)
                    continue;
            }

            var command = ToDomain(entity);
            command.MarkSucceeded(null);

            var updated = ToEntity(command);
            updated.ETag = entity.ETag;

            await _commands.UpdateAsync(updated, cancellationToken);
        }
    }

    private static int? ParseTargetVersion(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            var parsed = JsonSerializer.Deserialize<AgentConfigCommandPayload>(payload);

            return parsed?.TargetVersion;
        }
        catch (JsonException)
        {
            return null;
        }
    }


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
