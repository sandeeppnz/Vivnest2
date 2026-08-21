using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Abstractions.Data;

public interface IAgentHeartbeatWriter
{
    Task<AgentHeartbeatEntity> SaveAsync(
        Models.Agent.AgentHeartbeat heartbeat,
        CancellationToken cancellationToken = default);

    Task<Models.Agent.AgentHeartbeat?> GetAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default);
}
