namespace Vivnest.Cloud.Interfaces;

public interface IAgentEventRetentionService
{
    Task RunAsync(CancellationToken cancellationToken = default);
}
