using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IAgentCommandStore
{
    Task<AgentCommandEntity?> GetAsync(
        string tenantId,
        string siteId,
        string commandId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentCommandEntity>> GetByAgentAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default);

    // Decision-log.md ADR-079 - unpartitioned, same shape
    // IDeviceHeartbeatReader/IAgentHeartbeatReader's own GetAllAsync
    // already uses for HealthMonitorService's per-tick scans. Used by
    // CommandExpiryTimerFunction's sweep, which has no single
    // tenant/site to scope to.
    Task<IReadOnlyList<AgentCommandEntity>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        AgentCommandEntity entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        AgentCommandEntity entity,
        CancellationToken cancellationToken = default);
}
