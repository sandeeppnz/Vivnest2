namespace Vivnest.Core.Capabilities;

public interface ICapability
{
    CapabilityManifest Manifest { get; }

    CapabilityStatus Status { get; }

    Task StartAsync(
        ICapabilityContext context,
        CancellationToken cancellationToken);

    Task StopAsync(
        CancellationToken cancellationToken);
}
