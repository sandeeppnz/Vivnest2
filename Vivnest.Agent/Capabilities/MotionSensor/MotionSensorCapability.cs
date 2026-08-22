using Microsoft.Extensions.Logging;
using Vivnest.Abstraction.Agent.Capabilities;

namespace Vivnest.Agent.Capabilities.MotionSensor;

public sealed class MotionSensorCapability : ICapability
{
    private readonly MotionSensorMonitorWorker _worker;
    private readonly ILogger<MotionSensorCapability> _logger;
    private CapabilityStatus _status = CapabilityStatus.Registered;

    public MotionSensorCapability(
        MotionSensorMonitorWorker worker,
        ILogger<MotionSensorCapability> logger)
    {
        _worker = worker;
        _logger = logger;
    }

    public CapabilityStatus Status =>
        _status;

    public CapabilityManifest Manifest =>
        new()
        {
            Id = "motion.sensor",
            Name = "Motion Sensor",
            Version = "1.0.0",

            Commands = [],

            ProducedEvents =
            [
                new CapabilityEventDescriptor(
                    "motion.sensor.state.changed",
                    "1.0"),

                new CapabilityEventDescriptor(
                    "motion.sensor.reading.failed",
                    "1.0"),

                new CapabilityEventDescriptor(
                    "motion.sensor.battery.reported",
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