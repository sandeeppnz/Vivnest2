using Microsoft.Extensions.Hosting;

namespace Vivnest.Runtime.Capabilities;

public sealed class CapabilityHostedService
    : IHostedService
{
    private readonly CapabilityHost _host;

    public CapabilityHostedService(
        CapabilityHost host)
    {
        _host = host;
    }

    public Task StartAsync(
        CancellationToken cancellationToken)
    {
        return _host.StartAsync(
            cancellationToken);
    }

    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        return _host.StopAsync(
            cancellationToken);
    }
}
