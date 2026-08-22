namespace Vivnest.Abstraction.Agent.Capabilities;

public interface ICapability
{
    CapabilityManifest Manifest { get; }

    Task StartAsync(
        ICapabilityContext context,
        CancellationToken cancellationToken);

    Task StopAsync(
        CancellationToken cancellationToken);
}
