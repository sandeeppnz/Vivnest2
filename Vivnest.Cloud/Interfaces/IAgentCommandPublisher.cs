using Vivnest.Core.Queues.Models;

namespace Vivnest.Cloud.Interfaces;

public interface IAgentCommandPublisher
{
    Task PublishRestartCommandAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    Task PublishDeployCommandAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    Task PublishClassifyCommandAsync(
        ClassifyCaptureQueueMessage message,
        CancellationToken cancellationToken = default);
}
