using Microsoft.Extensions.Logging;
using Vivnest.Abstraction.Agent.Capabilities;

namespace Vivnest.Runtime.Capabilities;

public sealed class CapabilityHost
{
    private readonly ICapabilityRegistry _registry;
    private readonly ICapabilityContext _context;
    private readonly ILogger<CapabilityHost> _logger;

    public CapabilityHost(
        ICapabilityRegistry registry,
        ICapabilityContext context,
        ILogger<CapabilityHost> logger)
    {
        _registry = registry;
        _context = context;
        _logger = logger;
    }

    public async Task StartAsync(
        CancellationToken cancellationToken)
    {
        foreach (var capability in _registry.GetAll())
        {
            try
            {
                _logger.LogInformation(
                    "Starting capability {CapabilityId}",
                    capability.Manifest.Id);

                await capability.StartAsync(
                    _context,
                    cancellationToken);

                _logger.LogInformation(
                    "Capability {CapabilityId} started",
                    capability.Manifest.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to start capability {CapabilityId}",
                    capability.Manifest.Id);

                throw;
            }
        }
    }

    public async Task StopAsync(
        CancellationToken cancellationToken)
    {
        foreach (var capability in
                 _registry.GetAll().Reverse())
        {
            try
            {
                _logger.LogInformation(
                    "Stopping capability {CapabilityId}",
                    capability.Manifest.Id);

                await capability.StopAsync(
                    cancellationToken);

                _logger.LogInformation(
                    "Capability {CapabilityId} stopped",
                    capability.Manifest.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to stop capability {CapabilityId}",
                    capability.Manifest.Id);
            }
        }
    }
}