using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vivnest.Abstraction.Agent.Capabilities;
using Vivnest.Core.Utils;
using Vivnest.Runtime.Capabilities;

namespace Vivnest.Agent.Capabilities.MotionSensor;

public sealed class MotionSensorCapability : ICapability
{
    private readonly MotionSensorMonitorWorker _worker;
    private readonly IDeviceRuntimeStore _devices;
    private readonly ILogger<MotionSensorCapability> _logger;
    private CapabilityStatus _status = CapabilityStatus.Registered;

    public MotionSensorCapability(
        MotionSensorMonitorWorker worker,
        IDeviceRuntimeStore devices,
        ILogger<MotionSensorCapability> logger)
    {
        _worker = worker;
        _devices = devices;
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

            // ADR-103 - the capability owns its own precondition. An
            // enabled capability with nothing to act on is not healthy:
            // its worker would start, find no devices, return immediately,
            // and the capability would sit at Running forever while
            // nothing whatsoever happened. Fail here, with a diagnostic
            // that names the actual problem.
            //
            // Checked in the capability rather than the worker because a
            // generic BackgroundService has no business inventing a
            // CapabilityStatus, and because throwing from the worker would
            // report a configuration mistake as a stack trace.
            var deviceCount = _devices
                .GetDevices()
                .Count(d => d.Type == Vivnest.Core.Enums.DeviceType.MotionSensor);

            if (deviceCount == 0)
            {
                _status = CapabilityStatus.Failed;

                _logger.LogError(
                    "Capability {CapabilityId} cannot start: no motion sensors are assigned " +
                    "to this Agent. The Agent stays up; this capability is " +
                    "Failed until devices are assigned and it is restarted.",
                    Manifest.Id);

                return;
            }

            await _worker.StartAsync(
                cancellationToken);

            _status = CapabilityStatus.Running;

            // ADR-103 - StartAsync only gets the worker going; its
            // ExecuteAsync runs unobserved from here, and a fault in it
            // used to vanish silently. Watch it.
            //
            // After the status is set to Running, not before: the observer
            // reads it to tell a death from a shutdown, and a worker that
            // faults instantly would otherwise be judged against Starting.
            CapabilityWorkerSupervisor.Observe(
                _worker,
                Manifest.Id,
                _logger,
                isRunning: () => _status == CapabilityStatus.Running,
                markFailed: () => _status = CapabilityStatus.Failed);

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