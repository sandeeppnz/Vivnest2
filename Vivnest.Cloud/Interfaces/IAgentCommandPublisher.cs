using Vivnest.Core.Queues.Models;

namespace Vivnest.Cloud.Interfaces;

public interface IAgentCommandPublisher
{
    // Decision-log.md ADR-079 - commandId is null for the pre-existing
    // config-publish auto-restart path (TryEnqueueRestartAsync, no
    // tracked command involved), and set by CommandDispatcher for a real
    // tracked RestartAgent command.
    Task PublishRestartCommandAsync(
        string agentId,
        string? commandId = null,
        CancellationToken cancellationToken = default);

    // Decision-log.md ADR-073 - imageVersion is the target's active
    // AgentInstallation.ImageVersion, resolved by the caller before
    // publishing; null (the default) preserves the original "no version,
    // deploy :latest" behavior unchanged.
    Task PublishDeployCommandAsync(
        string agentId,
        string? imageVersion = null,
        CancellationToken cancellationToken = default);

    Task PublishClassifyCommandAsync(
        ClassifyCaptureQueueMessage message,
        CancellationToken cancellationToken = default);

    // Decision-log.md ADR-079 - the shared envelope for
    // RefreshConfiguration/ApplyConfiguration/ExecuteCapability, all
    // consumed by one new Agent-side worker (AgentCommandPollingWorker).
    Task PublishAgentCommandAsync(
        AgentCommandQueueMessage message,
        CancellationToken cancellationToken = default);
}
