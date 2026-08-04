namespace Vivnest.Cloud.Interfaces;

public interface IAgentCommandPublisher
{
    Task PublishRestartCommandAsync(
        string agentId,
        CancellationToken cancellationToken = default);
}
