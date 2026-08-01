using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IAgentHeartbeatReader
{
    Task<IReadOnlyList<AgentHeartbeatEntity>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<AgentHeartbeatEntity?> GetAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentHeartbeatEntity>> GetByTenantAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default);
}
