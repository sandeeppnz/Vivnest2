namespace Vivnest.Abstraction.Agent.Capabilities;

public interface ICapabilityWorker
{
    Task StartAsync(
        CancellationToken cancellationToken);

    Task StopAsync(
        CancellationToken cancellationToken);
}