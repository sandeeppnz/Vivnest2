using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Queues;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Cloud.Queues;

public sealed class AgentCommandPublisher : IAgentCommandPublisher
{
    // Literal, not config-driven - matches how Cloud.Functions' own
    // [QueueTrigger] attributes already use literal queue names (attribute
    // arguments must be compile-time constants). Keep in sync with
    // MessagingOptions.RestartCommandQueue (Agent-side) and
    // CommandPollingWorker if this ever changes.
    private const string RestartCommandQueueName = "agent-restart-commands";

    private readonly IQueuePublisher _queuePublisher;

    public AgentCommandPublisher(IQueuePublisher queuePublisher)
    {
        _queuePublisher = queuePublisher;
    }

    public Task PublishRestartCommandAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        return _queuePublisher.PublishAsync(
            RestartCommandQueueName,
            new RestartCommandQueueMessage(agentId, DateTime.UtcNow),
            cancellationToken);
    }
}
