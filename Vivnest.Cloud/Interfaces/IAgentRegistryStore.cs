using Vivnest.Core.DataStores.Entities;

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
