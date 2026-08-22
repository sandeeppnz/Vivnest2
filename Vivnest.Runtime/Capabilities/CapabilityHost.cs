using Microsoft.Extensions.Logging;
using Vivnest.Abstraction.Agent.Capabilities;

namespace Vivnest.Runtime.Capabilities;

public sealed class CapabilityHost
{
    private readonly ICapabilityRegistry _registry;
    private readonly ICapabilityContext _context;
    private readonly ILogger<CapabilityHost> _logger;
    private readonly IRuntimeCapabilityAssignmentStore _assignments;

    public CapabilityHost(
        ICapabilityRegistry registry,
        ICapabilityContext context,
        IRuntimeCapabilityAssignmentStore assignments,
        ILogger<CapabilityHost> logger)
    {
        _registry = registry;
        _context = context;
        _assignments = assignments;
        _logger = logger;
    }

    public async Task StartAsync(
        CancellationToken cancellationToken)
    {
        var registeredCapabilities =
            _registry
                .GetAll()
                .ToList();

        var enabledAssignments =
            _assignments
                .GetEnabled()
                .ToList();


        // ------------------------------------------------------------
        // Warn about capabilities assigned by Cloud but unavailable
        // in this Agent's registered capability implementations.
        // ------------------------------------------------------------


        foreach (var assignment in enabledAssignments)
        {
            var registered =
                registeredCapabilities.Any(capability =>
                    string.Equals(
                        capability.Manifest.Id,
                        assignment.CapabilityId,
                        StringComparison.OrdinalIgnoreCase));

            if (!registered)
            {
                _logger.LogWarning(
                    "Capability {CapabilityId} is enabled in runtime " +
                    "configuration but no implementation is registered " +
                    "in this Agent.",
                    assignment.CapabilityId);
            }
        }

        // ------------------------------------------------------------
        // Select only capabilities that are both:
        //
        // 1. Registered in this Agent
        // 2. Enabled in runtime configuration
        // ------------------------------------------------------------

        var capabilities =
            registeredCapabilities
                .Where(capability =>
                    enabledAssignments.Any(assignment =>
                        string.Equals(
                            assignment.CapabilityId,
                            capability.Manifest.Id,
                            StringComparison.OrdinalIgnoreCase)))
                .ToList();

        _logger.LogInformation(
            "Registered capabilities: {RegisteredCount}. " +
            "Enabled assignments: {AssignmentCount}. " +
            "Capabilities selected for startup: {SelectedCount}.",
            registeredCapabilities.Count,
            enabledAssignments.Count,
            capabilities.Count);


        // ------------------------------------------------------------
        // Start selected capabilities
        // ------------------------------------------------------------


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
            "Capability startup completed. " +
            "Started {StartedCount} capability(s).",
            capabilities.Count);
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