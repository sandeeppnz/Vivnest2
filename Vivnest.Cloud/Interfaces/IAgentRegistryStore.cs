using Vivnest.Core.DataStores.Entities;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IAgentRegistryStore
{
    Task<IReadOnlyList<AgentRegistryEntity>> ListAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default);

    Task<AgentRegistryEntity?> GetAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default);

    // Decision-log.md ADR-072 - the reverse lookup HealthMonitorService
    // needs: a heartbeat only ever carries the RuntimeAgentId (the space
    // AgentHeartbeatEntity is keyed by), never the admin AgentId this
    // store's own GetAsync expects. Null covers both "no Agent has this
    // RuntimeAgentId yet" and "more than one somehow does" identically -
    // either way there's no single unambiguous Agent to act on.
    Task<AgentRegistryEntity?> GetByRuntimeAgentIdAsync(
        string tenantId,
        string siteId,
        string runtimeAgentId,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        AgentRegistryEntity entity,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        AgentRegistryEntity entity,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default);
}
