using Vivnest.Core.Queues.Models;

namespace Vivnest.Cloud.Interfaces;

public interface IAgentCommandPublisher
{
    Task PublishRestartCommandAsync(
        string agentId,
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
}
