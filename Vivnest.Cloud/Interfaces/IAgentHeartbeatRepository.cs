using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Interfaces;

public interface IAgentHeartbeatRepository
{
    Task<IReadOnlyList<AgentHeartbeatEntity>> GetAllAsync(
        CancellationToken cancellationToken = default);
}
