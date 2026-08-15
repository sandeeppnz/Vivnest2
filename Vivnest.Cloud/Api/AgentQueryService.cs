using System.Text.Json;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Enums;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Api;

public sealed class AgentQueryService : IAgentQueryService
{
    private readonly IAgentHeartbeatReader _agentHeartbeats;
    private readonly IAgentEventReader _agentEvents;
    private readonly IAgentStatusResolver _statusResolver;
    private readonly IConfigurationSyncStatusService _configSyncStatus;
    private readonly IAgentVersionStatusService _versionStatus;
    private readonly IAgentRegistryStore _agentRegistry;

    public AgentQueryService(
        IAgentHeartbeatReader agentHeartbeats,
        IAgentEventReader agentEvents,
        IAgentStatusResolver statusResolver,
        IConfigurationSyncStatusService configSyncStatus,
        IAgentVersionStatusService versionStatus,
        IAgentRegistryStore agentRegistry)
    {
        _agentHeartbeats = agentHeartbeats;
        _agentEvents = agentEvents;
        _statusResolver = statusResolver;
        _configSyncStatus = configSyncStatus;
        _versionStatus = versionStatus;
        _agentRegistry = agentRegistry;
    }

    public async Task<IReadOnlyList<AgentSummaryDto>> GetAgentsAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default)
    {
        var entities = await _agentHeartbeats.GetByTenantAsync(
            tenant.TenantId,
            tenant.SiteId,
            cancellationToken);

        // Decision-log.md ADR-076 - one batch fetch of the whole registry,
        // not a per-row lookup, same "fetch once, dictionary lookup per
        // row" shape GetDevicesAsync already uses for its own agent
        // cross-reference.
        var registryEntities = await _agentRegistry.ListAsync(
            tenant.TenantId, tenant.SiteId, cancellationToken);

        var lifecycleByRuntimeId = registryEntities
            .Where(r => !string.IsNullOrWhiteSpace(r.RuntimeAgentId))
            .ToDictionary(r => r.RuntimeAgentId!, r => r.Status, StringComparer.Ordinal);

        var dtos = await Task.WhenAll(
            entities.Select(e => ToDtoAsync(
                tenant, e, lifecycleByRuntimeId.GetValueOrDefault(e.RowKey), cancellationToken)));

        return dtos.ToList();
    }

    public async Task<AgentSummaryDto?> GetAgentAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _agentHeartbeats.GetByTenantAsync(
            tenant.TenantId,
            tenant.SiteId,
            cancellationToken);

        var entity = entities.FirstOrDefault(e =>
            string.Equals(e.RowKey, agentId, StringComparison.Ordinal));

        if (entity == null)
            return null;

        var registryEntity = await _agentRegistry.GetByRuntimeAgentIdAsync(
            tenant.TenantId, tenant.SiteId, entity.RowKey, cancellationToken);

        return await ToDtoAsync(tenant, entity, registryEntity?.Status, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentMetricSampleDto>> GetAgentMetricsAsync(
        TenantContext tenant,
        string agentId,
        int days,
        CancellationToken cancellationToken = default)
    {
        var toUtc = DateTime.UtcNow;
        var fromUtc = toUtc.AddDays(-days);

        var entities = await _agentEvents.GetByAgentAndDateRangeAsync(
            tenant.TenantId,
            tenant.SiteId,
            agentId,
            eventType: AgentEventTypes.MetricsReported,
            fromUtc,
            toUtc,
            cancellationToken);

        return entities.Select(ToMetricSampleDto).ToList();
    }

    private static AgentMetricSampleDto ToMetricSampleDto(AgentEventEntity entity)
    {
        double? cpuUsagePercent = null;
        long memoryUsedBytes = 0;
        long bytesUploaded = 0;

        try
        {
            using var doc = JsonDocument.Parse(entity.Payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("CpuUsagePercent", out var cpuProp) && cpuProp.ValueKind != JsonValueKind.Null)
                cpuUsagePercent = cpuProp.GetDouble();

            if (root.TryGetProperty("MemoryUsedBytes", out var memProp))
                memoryUsedBytes = memProp.GetInt64();

            if (root.TryGetProperty("BytesUploaded", out var bytesProp))
                bytesUploaded = bytesProp.GetInt64();
        }
        catch (JsonException)
        {
            // Leave defaults if the payload isn't valid JSON.
        }

        return new AgentMetricSampleDto(entity.OccurredAtUtc, cpuUsagePercent, memoryUsedBytes, bytesUploaded);
    }

    // Decision-log.md ADR-074 - calls the same IAgentStatusResolver
    // HealthMonitorService uses to drive the offline/recovery
    // notification, so the dashboard's Status/StatusSinceUtc can never
    // drift from what actually triggers an alert (this used to be a
    // hand-mirrored copy of that threshold logic, exactly the kind of
    // duplication IDeviceStatusResolver was already extracted to avoid).
    // ConfigurationStatus/VersionStatus (ADR-075) go through the
    // lightweight heartbeat-based overloads, not the full-projection
    // methods - cheap enough to compute per row at this scale (a handful
    // of agents), same reasoning DeviceCapabilitiesQueryService's own O(N)
    // scan already uses. lifecycleStatus (ADR-076) is the Admin-set
    // AgentRegistryStatus ("Active"/"Inactive"), resolved by the caller -
    // kept as its own field, never collapsed into Status, per the spec's
    // own "DeviceStatus = Disabled, OperationalStatus = N/A" example:
    // Inactive forces the *operational* status to NotApplicable rather
    // than letting a heartbeat that stopped updating when the agent was
    // deactivated read as a misleading Offline.
    private async Task<AgentSummaryDto> ToDtoAsync(
        TenantContext tenant, AgentHeartbeatEntity entity, string? lifecycleStatus, CancellationToken cancellationToken)
    {
        var heartbeatInterval = TableTimeSpan.Parse(entity.HeartbeatInterval);
        var (resolvedStatus, statusSinceUtc) = _statusResolver.Determine(entity);

        var status = string.Equals(lifecycleStatus, "Inactive", StringComparison.Ordinal)
            ? DeviceHeartbeatStatus.NotApplicable
            : resolvedStatus;

        var configStatus = await _configSyncStatus.GetAgentStatusFromHeartbeatAsync(
            tenant, entity, cancellationToken);

        var versionStatus = await _versionStatus.GetStatusForRuntimeAgentAsync(
            tenant, entity.RowKey, entity.FirmwareVersion, cancellationToken);

        return new AgentSummaryDto(
            AgentId: entity.RowKey,
            Name: entity.Name,
            HostName: entity.HostName,
            FirmwareVersion: entity.FirmwareVersion,
            RuntimeVersion: entity.RuntimeVersion,
            OsDescription: entity.OsDescription,
            Status: status.ToString(),
            StartedUtc: entity.StartedUtc,
            LastHeartbeatUtc: entity.LastHeartbeatUtc,
            HeartbeatInterval: heartbeatInterval,
            // Never actually null here - the resolver only returns null
            // StatusSinceUtc for a missing heartbeat entity, and entity is
            // always real in this context - the fallback exists purely to
            // satisfy AgentSummaryDto's non-nullable field.
            StatusSinceUtc: statusSinceUtc ?? entity.LastHeartbeatUtc,
            TenantId: entity.TenantId,
            SiteId: entity.SiteId,
            Error: entity.Error,
            ConfigurationStatus: configStatus,
            VersionStatus: versionStatus,
            LifecycleStatus: lifecycleStatus);
    }
}
