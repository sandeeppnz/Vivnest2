using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Cloud.Admin;

// Decision-log.md ADR-079 - Admin -> Command -> Agent's single entry
// point: validate, persist, enqueue. Deliberately writes the command row
// exactly once per DispatchAsync call (already reflecting whatever
// terminal-for-this-request state applies - Dispatched on a successful
// enqueue, Failed on a rejected/failed one) rather than
// Create-then-Update - avoids the exact ETag-after-UpsertAsync gap
// AzureTableStore<T>.UpdateAsync's own ADR-077 fix addressed for a
// different call site (UpsertAsync still doesn't capture the response
// ETag, so a same-method Update immediately after a Create would hit
// that gap again). Every later transition (Received/Executing/Succeeded/
// Failed, reported back by the Agent) goes through
// AgentCommandManagementService instead, which always re-fetches before
// updating - the safe pattern every other management service here
// already uses.
public sealed class CommandDispatcher : ICommandDispatcher
{
    // Spec's own worked example (Created 10:00, Expires 10:05) - kept
    // uniform across all four command types rather than tuned per type,
    // per the plan's own "don't make this more complicated than
    // necessary" call.
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(5);

    private static readonly HashSet<string> DisruptiveCommandTypes = new(StringComparer.Ordinal)
    {
        AgentCommandTypes.RestartAgent,
        AgentCommandTypes.ApplyConfiguration
    };

    private readonly IAgentCommandStore _commands;
    private readonly IAgentCommandPublisher _publisher;
    private readonly IAgentQueryService _agentQueryService;
    private readonly IDeviceQueryService _deviceQueryService;
    private readonly IDeviceCapabilityStore _deviceCapabilities;

    public CommandDispatcher(
        IAgentCommandStore commands,
        IAgentCommandPublisher publisher,
        IAgentQueryService agentQueryService,
        IDeviceQueryService deviceQueryService,
        IDeviceCapabilityStore deviceCapabilities)
    {
        _commands = commands;
        _publisher = publisher;
        _agentQueryService = agentQueryService;
        _deviceQueryService = deviceQueryService;
        _deviceCapabilities = deviceCapabilities;
    }

    public async Task<AgentCommandDto?> DispatchAsync(
        TenantContext tenant,
        string commandType,
        string targetAgentId,
        string requestedBy,
        string? targetDeviceId = null,
        string? capabilityId = null,
        string? payload = null,
        CancellationToken cancellationToken = default)
    {
        // Tenant-scoped existence check, same reasoning/shape as
        // AgentsFunction.RestartAgent's own pre-existing check - without
        // it, any valid tenant key could target an agentId belonging to
        // a different tenant just by guessing/knowing its id.
        var agent = await _agentQueryService.GetAgentAsync(tenant, targetAgentId, cancellationToken);

        if (agent == null)
            return null;

        var (errorCode, errorMessage) = await ValidateAsync(
            tenant, commandType, targetAgentId, targetDeviceId, capabilityId, cancellationToken);

        if (errorCode == null && DisruptiveCommandTypes.Contains(commandType))
        {
            var busy = await IsAgentBusyAsync(tenant, targetAgentId, cancellationToken);

            if (busy)
            {
                errorCode = "AGENT_BUSY";
                errorMessage = $"Agent {targetAgentId} already has a disruptive command in progress.";
            }
        }

        var command = new AgentCommand(
            tenant.TenantId,
            tenant.SiteId,
            targetAgentId,
            commandType,
            DefaultExpiry,
            requestedBy,
            targetDeviceId,
            capabilityId,
            payload);

        if (errorCode != null)
        {
            command.MarkFailed(errorCode, errorMessage!);

            var failedEntity = ToEntity(command);
            await _commands.CreateAsync(failedEntity, cancellationToken);

            return ToDto(failedEntity);
        }

        try
        {
            if (string.Equals(commandType, AgentCommandTypes.RestartAgent, StringComparison.Ordinal))
            {
                await _publisher.PublishRestartCommandAsync(targetAgentId, command.CommandId, cancellationToken);
            }
            else
            {
                await _publisher.PublishAgentCommandAsync(
                    new AgentCommandQueueMessage(command.CommandId, targetAgentId, commandType),
                    cancellationToken);
            }

            command.MarkDispatched();
        }
        catch (Exception ex)
        {
            command.MarkFailed("ENQUEUE_FAILED", ex.Message);
        }

        var entity = ToEntity(command);
        await _commands.CreateAsync(entity, cancellationToken);

        return ToDto(entity);
    }

    // Decision-log.md ADR-079 - the full Tenant/Site/Agent/Device/
    // Capability chain from the Phase 9 spec's own section 16/17,
    // symmetric between Built-in (ImageCapture, ownership-based) and
    // Derived (DeviceCapability/ExecutingAgentId-based) capabilities.
    // Returns (null, null) when nothing's wrong; a non-null error code
    // means the command should still be created, just straight into
    // Failed (see DispatchAsync's own comment for why).
    private async Task<(string? ErrorCode, string? ErrorMessage)> ValidateAsync(
        TenantContext tenant,
        string commandType,
        string targetAgentId,
        string? targetDeviceId,
        string? capabilityId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(commandType, AgentCommandTypes.ExecuteCapability, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(targetDeviceId) || string.IsNullOrWhiteSpace(capabilityId))
                return ("INVALID_REQUEST", "ExecuteCapability requires TargetDeviceId and CapabilityId.");

            var device = await _deviceQueryService.GetDeviceAsync(tenant, targetDeviceId, cancellationToken);

            if (device == null)
                return ("DEVICE_NOT_FOUND", $"Device {targetDeviceId} was not found.");

            if (string.Equals(capabilityId, AgentCommandTypes.ImageCaptureCapabilityId, StringComparison.Ordinal))
            {
                if (!string.Equals(device.AgentId, targetAgentId, StringComparison.Ordinal))
                    return ("WRONG_AGENT", $"Device {targetDeviceId} is owned by a different agent.");

                return (null, null);
            }

            var assignment = await _deviceCapabilities.GetActiveByDeviceAndCapabilityAsync(
                tenant.TenantId, tenant.SiteId, targetDeviceId, capabilityId, cancellationToken);

            if (assignment == null)
                return ("CAPABILITY_NOT_ASSIGNED", $"Capability {capabilityId} is not assigned to device {targetDeviceId}.");

            if (!string.Equals(assignment.ExecutingAgentId, targetAgentId, StringComparison.Ordinal))
                return ("WRONG_EXECUTING_AGENT", $"Capability {capabilityId} on device {targetDeviceId} executes on a different agent.");

            return (null, null);
        }

        // ApplyConfiguration's optional TargetDeviceId (absent = target
        // the Agent's own config) - same ownership check as ImageCapture
        // above, just without the CapabilityId branch.
        if (!string.IsNullOrWhiteSpace(targetDeviceId))
        {
            var device = await _deviceQueryService.GetDeviceAsync(tenant, targetDeviceId, cancellationToken);

            if (device == null)
                return ("DEVICE_NOT_FOUND", $"Device {targetDeviceId} was not found.");

            if (!string.Equals(device.AgentId, targetAgentId, StringComparison.Ordinal))
                return ("WRONG_AGENT", $"Device {targetDeviceId} is owned by a different agent.");
        }

        return (null, null);
    }

    private async Task<bool> IsAgentBusyAsync(
        TenantContext tenant,
        string targetAgentId,
        CancellationToken cancellationToken)
    {
        var existing = await _commands.GetByAgentAsync(
            tenant.TenantId, tenant.SiteId, targetAgentId, cancellationToken);

        return existing.Any(c =>
            DisruptiveCommandTypes.Contains(c.CommandType) &&
            (c.Status == AgentCommandStatus.Received.ToString() ||
             c.Status == AgentCommandStatus.Executing.ToString()));
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

    internal static AgentCommandDto ToDto(AgentCommandEntity entity)
    {
        return new AgentCommandDto(
            entity.RowKey,
            entity.AgentId,
            entity.TargetDeviceId,
            entity.CapabilityId,
            entity.CommandType,
            entity.Status,
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
            entity.ExpiresUtc,
            entity.TenantId,
            entity.SiteId);
    }
}
