using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Vivnest.Core.Capabilities;

namespace Vivnest.Runtime.Capabilities;

public sealed class CapabilityHost
{
    private readonly ICapabilityRegistry _registry;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _services;
    private readonly ILogger<CapabilityHost> _logger;
    private readonly IRuntimeCapabilityAssignmentStore _assignments;

    public CapabilityHost(
        ICapabilityRegistry registry,
        IConfiguration configuration,
        IServiceProvider services,
        IRuntimeCapabilityAssignmentStore assignments,
        ILogger<CapabilityHost> logger)
    {
        _registry = registry;
        _configuration = configuration;
        _services = services;
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

        // A join, not a filter (ADR-101). This used to be
        // .Where(... .Any(...)), which answers "does an assignment exist?"
        // and discards WHICH one - so the assignment was known here and
        // thrown away, and every capability then started with the same
        // shared context. Written as an explicit loop rather than a LINQ
        // join because the ids match case-insensitively and a join would
        // quietly use the default comparer.
        var selected =
            new List<(ICapability Capability, RuntimeCapabilityAssignment Assignment)>();

        foreach (var capability in registeredCapabilities)
        {
            var assignment =
                enabledAssignments.FirstOrDefault(x =>
                    string.Equals(
                        x.CapabilityId,
                        capability.Manifest.Id,
                        StringComparison.OrdinalIgnoreCase));

            if (assignment == null)
                continue;

            selected.Add((capability, assignment));
        }

        var capabilities = selected.Select(x => x.Capability).ToList();

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


        foreach (var (capability, assignment) in selected)
        {
            try
            {
                // The host owns the pairing, so the host is where a
                // mismatch has to be caught. Starting a capability with
                // another capability's assignment would be near-invisible
                // at runtime - it would simply behave as if configured by
                // the wrong entry.
                if (!string.Equals(
                        capability.Manifest.Id,
                        assignment.CapabilityId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Capability assignment mismatch. " +
                        $"Manifest='{capability.Manifest.Id}', " +
                        $"Assignment='{assignment.CapabilityId}'.");
                }

                _logger.LogInformation(
                    "Starting capability {CapabilityId} " +
                    "version {CapabilityVersion}.",
                    capability.Manifest.Id,
                    capability.Manifest.Version);

                await capability.StartAsync(
                    new CapabilityContext(_configuration, _services, assignment),
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
                // Logged and CONTINUED, not rethrown (ADR-116, closing the
                // question ADR-095 left open). Rethrowing propagated
                // through CapabilityHostedService into host startup and
                // took the whole Agent down - platform workers included -
                // so a camera device store hiccup at boot silenced the
                // heartbeats too, and Cloud saw "Agent offline" instead of
                // "camera.capture failed". That erased exactly the
                // distinction ADR-103 exists to preserve.
                //
                // The deferral reason has expired: this LogError becomes an
                // ErrorLogged AgentEvent (Sprint 8), throttled and
                // notified, and the capability sits at Failed on the
                // ADR-103 health surface - a swallowed startup failure is
                // no longer invisible, it alerts by name.
                _logger.LogError(
                    ex,
                    "Failed to start capability {CapabilityId} " +
                    "(status: {Status}). The Agent stays up; other " +
                    "capabilities are unaffected.",
                    capability.Manifest.Id,
                    capability.Status);
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
        // Registry order. A .Reverse() sat here implying reverse-startup
        // shutdown, but it reversed a Dictionary's value order - which is
        // not startup order or any defined order - so the guarantee it
        // suggested never existed. Capabilities are independent of each
        // other today; if ordered shutdown ever matters, it needs the real
        // startup sequence recorded, not a reversed hash order. Stopping
        // ALL registered capabilities (not just the started subset) is
        // deliberate and safe: StopAsync on a never-started capability is
        // a no-op by DeviceCapabilityBase's own re-entrancy guard.
        var capabilities = _registry
            .GetAll()
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