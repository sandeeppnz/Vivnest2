using Vivnest.Core.Queues.Models;

namespace Vivnest.Cloud.Interfaces;

public interface IAgentEventQueueHandler
{
    Task HandleAsync(
        AgentEventQueueMessage message,
        CancellationToken cancellationToken = default);
}
