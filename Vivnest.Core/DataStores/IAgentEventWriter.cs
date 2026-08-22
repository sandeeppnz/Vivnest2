using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;

namespace Vivnest.Core.DataStores;

public interface IAgentEventWriter
{
    Task<AgentEventEntity?> SaveAsync(
        AgentEvent agentEvent,
        CancellationToken cancellationToken = default);
}
