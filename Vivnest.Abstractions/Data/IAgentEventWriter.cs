namespace Vivnest.Abstractions.Data;

public interface IAgentEventWriter
{
    Task<Core.DataStores.Entities.AgentEventEntity?> SaveAsync(
        Models.Agent.AgentEvent agentEvent,
        CancellationToken cancellationToken = default);
}
