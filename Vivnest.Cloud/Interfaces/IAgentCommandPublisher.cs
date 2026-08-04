namespace Vivnest.Cloud.Interfaces;

public interface IAgentCommandPublisher
{
    Task PublishRestartCommandAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    Task PublishDeployCommandAsync(
        string agentId,
        CancellationToken cancellationToken = default);
}
