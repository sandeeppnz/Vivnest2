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
        var capabilities = _registry
            .GetAll()
            .ToList();

        _logger.LogInformation(
            "Starting {CapabilityCount} capability(s).",
            capabilities.Count);

        foreach (var capability in capabilities)
        {
            try
            {
                _logger.LogInformation(
                    "Starting capability {CapabilityId} " +
                    "version {CapabilityVersion}.",
                    capability.Manifest.Id,
                    capability.Manifest.Version);

                await capability.StartAsync(
                    _context,
                    cancellationToken);

                _logger.LogInformation(
                    "Capability {CapabilityId} is now {Status}.",
                    capability.Manifest.Id,
                    capability.Status);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Capability startup cancelled for {CapabilityId}.",
                    capability.Manifest.Id);

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to start capability {CapabilityId}. " +
                    "Capability status: {Status}.",
                    capability.Manifest.Id,
                    capability.Status);

                throw;
            }
        }

        _logger.LogInformation(
            "All capabilities started.");
    }

    public async Task StopAsync(
        CancellationToken cancellationToken)
    {
        var capabilities = _registry
            .GetAll()
            .Reverse()
            .ToList();

        _logger.LogInformation(
            "Stopping {CapabilityCount} capability(s).",
            capabilities.Count);

        foreach (var capability in capabilities)
        {
            try
            {
                _logger.LogInformation(
                    "Stopping capability {CapabilityId}.",
                    capability.Manifest.Id);

                await capability.StopAsync(
                    cancellationToken);

                _logger.LogInformation(
                    "Capability {CapabilityId} is now {Status}.",
                    capability.Manifest.Id,
                    capability.Status);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Capability shutdown cancelled for {CapabilityId}.",
                    capability.Manifest.Id);

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to stop capability {CapabilityId}. " +
                    "Capability status: {Status}.",
                    capability.Manifest.Id,
                    capability.Status);
            }
        }

        _logger.LogInformation(
            "Capability shutdown completed.");
    }
}