using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;

namespace Vivnest.Core.DataStores;

public interface IAgentHeartbeatWriter
{
    Task<AgentHeartbeatEntity> SaveAsync(
        AgentHeartbeat heartbeat,
        CancellationToken cancellationToken = default);

    Task<AgentHeartbeat?> GetAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default);
}
