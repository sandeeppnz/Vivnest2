using System.Text.Json;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Api;

public sealed class AgentQueryService : IAgentQueryService
{
    private readonly IAgentHeartbeatReader _agentHeartbeats;
    private readonly IAgentEventReader _agentEvents;
    private readonly IAgentStatusResolver _statusResolver;

    public AgentQueryService(
        IAgentHeartbeatReader agentHeartbeats,
        IAgentEventReader agentEvents,
        IAgentStatusResolver statusResolver)
    {
        _agentHeartbeats = agentHeartbeats;
        _agentEvents = agentEvents;
        _statusResolver = statusResolver;
    }

    public async Task<IReadOnlyList<AgentSummaryDto>> GetAgentsAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default)
    {
        var entities = await _agentHeartbeats.GetByTenantAsync(
            tenant.TenantId,
            tenant.SiteId,
            cancellationToken);

        return entities.Select(ToDto).ToList();
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

        return entity == null ? null : ToDto(entity);
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
    private AgentSummaryDto ToDto(AgentHeartbeatEntity entity)
    {
        var heartbeatInterval = TableTimeSpan.Parse(entity.HeartbeatInterval);
        var (status, statusSinceUtc) = _statusResolver.Determine(entity);

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
            Error: entity.Error);
    }
}
