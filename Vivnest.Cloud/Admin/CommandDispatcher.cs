using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Queues.Models;
using Vivnest.Core.Storage;

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
    private readonly IAgentRegistryStore _agentRegistry;
    private readonly AzureTableStore<AgentConfigurationEntity> _agentConfigurations;

    public CommandDispatcher(
        IAgentCommandStore commands,
        IAgentCommandPublisher publisher,
        IAgentQueryService agentQueryService,
        IDeviceQueryService deviceQueryService,
        IDeviceCapabilityStore deviceCapabilities,
        IAgentRegistryStore agentRegistry,
        TableServiceClient tableServiceClient,
        IOptions<TablesOptions> tablesOptions)
    {
        _commands = commands;
        _publisher = publisher;
        _agentQueryService = agentQueryService;
        _deviceQueryService = deviceQueryService;
        _deviceCapabilities = deviceCapabilities;
        _agentRegistry = agentRegistry;
        _agentConfigurations = new AzureTableStore<AgentConfigurationEntity>(
            tableServiceClient, tablesOptions.Value.AgentConfiguration);
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
            tenant, commandType, targetAgentId, targetDeviceId, capabilityId, payload, cancellationToken);

        if (errorCode == null && DisruptiveCommandTypes.Contains(commandType))
        {
            var busy = await IsAgentBusyAsync(tenant, targetAgentId, cancellationToken);

            if (busy)
            {
                errorCode = "AGENT_BUSY";
                errorMessage = $"Agent {targetAgentId} already has a disruptive command in progress.";
            }
        }

        // Decision-log.md ADR-080 - RefreshConfiguration/ApplyConfiguration
        // both resolve to "the Agent should be running configuration
        // version N" by dispatch time: Refresh resolves N from whatever's
        // currently published (ValidateAsync doesn't reject Refresh, so
        // this only runs once errorCode is already known null); Apply's
        // caller-supplied version was already validated to exist by
        // ValidateAsync above. Either way the command row is persisted
        // with this normalized payload, not the caller's raw input -
        // AgentCommandManagementService's completion hook and the Agent's
        // own command handlers both read this one shape.
        var resolvedPayload = payload;

        if (errorCode == null &&
            (string.Equals(commandType, AgentCommandTypes.RefreshConfiguration, StringComparison.Ordinal) ||
             string.Equals(commandType, AgentCommandTypes.ApplyConfiguration, StringComparison.Ordinal)))
        {
            var targetVersion = string.Equals(commandType, AgentCommandTypes.RefreshConfiguration, StringComparison.Ordinal)
                ? await ResolveCurrentAgentConfigVersionAsync(tenant.TenantId, tenant.SiteId, targetAgentId, cancellationToken)
                : ParseRequestedVersion(payload)!.Value;

            resolvedPayload = JsonSerializer.Serialize(new AgentConfigCommandPayload(targetVersion));
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
            resolvedPayload);

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
        string? payload,
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

            // Decision-log.md ADR-081 - a real bug, found live:
            // DeviceCapability.ExecutingAgentId lives in the admin AgentId
            // identity space (validated by CapabilityAssignmentService
            // against IAgentRegistryStore, the same boundary
            // DeviceService.IsValidOwningAgentAsync uses for
            // Device.OwningAgentId) - but targetAgentId here is always a
            // RuntimeAgentId (the identity space every /agents/{agentId}
            // route and DispatchAsync's own GetAgentAsync ownership check
            // already use). Comparing them directly, as Pass 1's original
            // code did, would never match for ANY real, correctly-assigned
            // capability - reverse-resolve targetAgentId to its admin
            // AgentId first, the same GetByRuntimeAgentIdAsync lookup
            // AgentQueryService/AgentInstallationManagementService already
            // use for this exact identity-space crossing.
            var registryEntity = await _agentRegistry.GetByRuntimeAgentIdAsync(
                tenant.TenantId, tenant.SiteId, targetAgentId, cancellationToken);

            if (registryEntity == null || !string.Equals(assignment.ExecutingAgentId, registryEntity.RowKey, StringComparison.Ordinal))
                return ("WRONG_EXECUTING_AGENT", $"Capability {capabilityId} on device {targetDeviceId} executes on a different agent.");

            return (null, null);
        }

        // Decision-log.md ADR-080 - Phase 9 Pass 2, scoped to the Agent's
        // own configuration only (no TargetDeviceId support this pass -
        // see the ADR for why). Reuses RollbackAsync's own precedent for
        // "does version N exist": compare against
        // AgentConfigurationEntity.CurrentVersion rather than a Blob
        // round-trip, since versions are only ever created monotonically
        // and never deleted (RollbackAsync itself never deletes a version
        // blob either), so 1..CurrentVersion is exactly the set of
        // versions that exist.
        if (string.Equals(commandType, AgentCommandTypes.ApplyConfiguration, StringComparison.Ordinal))
        {
            var requestedVersion = ParseRequestedVersion(payload);

            if (requestedVersion is null || requestedVersion < 1)
                return ("INVALID_REQUEST", "ApplyConfiguration requires a valid ConfigurationVersion in the payload.");

            var currentVersion = await ResolveCurrentAgentConfigVersionAsync(
                tenant.TenantId, tenant.SiteId, targetAgentId, cancellationToken);

            if (requestedVersion > currentVersion)
                return ("VERSION_NOT_FOUND",
                    $"Configuration version {requestedVersion} does not exist (current published version is {currentVersion}).");

            return (null, null);
        }

        // ApplyConfiguration's optional TargetDeviceId (absent = target
        // the Agent's own config) - same ownership check as ImageCapture
        // above, just without the CapabilityId branch. Dead code for
        // ApplyConfiguration today (Pass 2 never passes a TargetDeviceId
        // for it - see above), left in place as groundwork for whenever
        // device-level Apply/Refresh support is added.
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

    // Decision-log.md ADR-080 - shared by ValidateAsync's version-exists
    // check and DispatchAsync's own TargetVersion resolution for
    // RefreshConfiguration, so both read the exact same published-version
    // signal rather than two independently-drifting lookups.
    private async Task<int> ResolveCurrentAgentConfigVersionAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken)
    {
        var entity = await _agentConfigurations.GetAsync(
            new SiteScope(tenantId, siteId).PartitionKey, agentId, cancellationToken);

        return entity?.CurrentVersion ?? 0;
    }

    private static int? ParseRequestedVersion(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            var parsed = JsonSerializer.Deserialize<ApplyConfigurationRequest>(payload);

            return parsed?.ConfigurationVersion;
        }
        catch (JsonException)
        {
            return null;
        }
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
