using Microsoft.Extensions.Logging;
using Vivnest.Abstraction.Agent.Capabilities;

namespace Vivnest.Agent.Capabilities.SmartPlug;

public sealed class SmartPlugCapability : ICapability
{
    private readonly SmartPlugMonitorWorker _worker;
    private readonly ILogger<SmartPlugCapability> _logger;

    private CapabilityStatus _status = CapabilityStatus.Registered;

    public SmartPlugCapability(
        SmartPlugMonitorWorker worker,
        ILogger<SmartPlugCapability> logger)
    {
        _worker = worker;
        _logger = logger;
    }

    public CapabilityStatus Status =>
        _status;

    public CapabilityManifest Manifest =>
        new()
        {
            Id = "smartplug.monitor",
            Name = "Smart Plug",
            Version = "1.0.0",

            Commands = [],

            ProducedEvents =
            [
                new CapabilityEventDescriptor(
                    "smartplug.reading.completed",
                    "1.0"),

                new CapabilityEventDescriptor(
                    "smartplug.reading.failed",
                    "1.0"),

                new CapabilityEventDescriptor(
                    "smartplug.power.state.changed",
                    "1.0")
            ],

            ConsumedEvents = [],

            Dependencies = []
        };

    public async Task StartAsync(
        ICapabilityContext context,
        CancellationToken cancellationToken)
    {
        if (_status is
            CapabilityStatus.Starting or
            CapabilityStatus.Running)
        {
            return;
        }

        _status = CapabilityStatus.Starting;

        try
        {
            _logger.LogInformation(
                "Starting capability {CapabilityId} for agent {AgentId}.",
                Manifest.Id,
                context.AgentId);

            await _worker.StartAsync(
                cancellationToken);

            _status = CapabilityStatus.Running;

            _logger.LogInformation(
                "Capability {CapabilityId} started.",
                Manifest.Id);
        }
        catch
        {
            _status = CapabilityStatus.Failed;

            throw;
        }
    }

    public async Task StopAsync(
        CancellationToken cancellationToken)
    {
        if (_status is
            CapabilityStatus.Stopped or
            CapabilityStatus.Registered)
        {
            return;
        }

        _status = CapabilityStatus.Stopping;

        try
        {
            _logger.LogInformation(
                "Stopping capability {CapabilityId}.",
                Manifest.Id);

            await _worker.StopAsync(
                cancellationToken);

            _status = CapabilityStatus.Stopped;

            _logger.LogInformation(
                "Capability {CapabilityId} stopped.",
                Manifest.Id);
        }
        catch
        {
            _status = CapabilityStatus.Failed;

            throw;
        }
    }
}
