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

    // Same reasoning, consumed by Vivnest.Agent.Updater instead of
    // CommandPollingWorker - keep in sync with
    // MessagingOptions.DeployCommandQueue and Updater's own queue name.
    private const string DeployCommandQueueName = "agent-deploy-commands";

    // Same reasoning, consumed by a High-type agent's repurposed
    // SinkCleanlinessWorker (ADR-035) - keep in sync with
    // MessagingOptions.ClassifyCommandQueue.
    private const string ClassifyCommandQueueName = "agent-classify-commands";

    // Decision-log.md ADR-079 - the shared envelope queue for
    // RefreshConfiguration/ApplyConfiguration/ExecuteCapability, consumed
    // by AgentCommandPollingWorker (same-consumer case, see ADR-024's
    // actual "one queue per consumer" rule) - keep in sync with
    // MessagingOptions.AgentCommandQueue.
    private const string AgentCommandQueueName = "agent-commands";

    private readonly IQueuePublisher _queuePublisher;

    public AgentCommandPublisher(IQueuePublisher queuePublisher)
    {
        _queuePublisher = queuePublisher;
    }

    public Task PublishRestartCommandAsync(
        string agentId,
        string? commandId = null,
        CancellationToken cancellationToken = default)
    {
        return _queuePublisher.PublishAsync(
            RestartCommandQueueName,
            new RestartCommandQueueMessage(agentId, DateTime.UtcNow, commandId),
            cancellationToken);
    }

    public Task PublishAgentCommandAsync(
        AgentCommandQueueMessage message,
        CancellationToken cancellationToken = default)
    {
        return _queuePublisher.PublishAsync(
            AgentCommandQueueName,
            message,
            cancellationToken);
    }

    public Task PublishDeployCommandAsync(
        string agentId,
        string? imageVersion = null,
        CancellationToken cancellationToken = default)
    {
        return _queuePublisher.PublishAsync(
            DeployCommandQueueName,
            new DeployCommandQueueMessage(agentId, DateTime.UtcNow, imageVersion),
            cancellationToken);
    }

    // Relays the message through unchanged - it already carries
    // everything the High-type agent needs (ADR-035), unlike Restart/Deploy
    // which only need an AgentId.
    public Task PublishClassifyCommandAsync(
        ClassifyCaptureQueueMessage message,
        CancellationToken cancellationToken = default)
    {
        return _queuePublisher.PublishAsync(
            ClassifyCommandQueueName,
            message,
            cancellationToken);
    }
}
