using Microsoft.Extensions.Options;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Api;

public sealed class AgentQueryService : IAgentQueryService
{
    private static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromMinutes(5);

    private readonly IAgentHeartbeatReader _agentHeartbeats;
    private readonly HealthMonitorOptions _options;

    public AgentQueryService(
        IAgentHeartbeatReader agentHeartbeats,
        IOptions<HealthMonitorOptions> options)
    {
        _agentHeartbeats = agentHeartbeats;
        _options = options.Value;
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

    // Mirrors HealthMonitorService.IsAgentOffline (no AgentStaleMultiplier -
    // that buffer only applies to the device-cascade check), so the
    // dashboard's Status/StatusSinceUtc agree with what actually drives the
    // agent-level notification and LastRecoveredUtc.
    private AgentSummaryDto ToDto(AgentHeartbeatEntity entity)
    {
        var heartbeatInterval = TableTimeSpan.Parse(entity.HeartbeatInterval);

        var staleAfter = heartbeatInterval > TimeSpan.Zero
            ? heartbeatInterval
            : DefaultStaleAfter;

        var elapsed = DateTime.UtcNow - entity.LastHeartbeatUtc;
        var isOffline = elapsed > staleAfter;
        var status = isOffline ? "Offline" : "Online";

        // Offline since its last confirmed-alive heartbeat. Online since its
        // last recorded recovery, or - if it's never actually been marked
        // offline (LastRecoveredUtc never set) - since this process started.
        var statusSinceUtc = isOffline
            ? entity.LastHeartbeatUtc
            : entity.LastRecoveredUtc ?? entity.StartedUtc;

        return new AgentSummaryDto(
            AgentId: entity.RowKey,
            HostName: entity.HostName,
            Status: status,
            StartedUtc: entity.StartedUtc,
            LastHeartbeatUtc: entity.LastHeartbeatUtc,
            HeartbeatInterval: heartbeatInterval,
            StatusSinceUtc: statusSinceUtc,
            TenantId: entity.TenantId,
            SiteId: entity.SiteId,
            Error: entity.Error);
    }
}
