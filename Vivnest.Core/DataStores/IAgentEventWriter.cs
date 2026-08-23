using Vivnest.Core.DataStores.Entities;
using Vivnest.Domain.Agents;

namespace Vivnest.Core.DataStores;

public interface IAgentEventWriter
{
    Task<AgentEventEntity?> SaveAsync(
        AgentEvent agentEvent,
        CancellationToken cancellationToken = default);
}
