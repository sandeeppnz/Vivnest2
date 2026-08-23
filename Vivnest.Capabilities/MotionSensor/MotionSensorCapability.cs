using Microsoft.Extensions.Logging;
using Vivnest.Core.Capabilities;
using Vivnest.Core.Utils;
using Vivnest.Domain.Devices;

namespace Vivnest.Capabilities.MotionSensor;

public sealed class MotionSensorCapability : DeviceCapabilityBase
{
    public MotionSensorCapability(
        MotionSensorMonitorWorker worker,
        IDeviceRuntimeStore devices,
        ILogger<MotionSensorCapability> logger)
        : base(worker, devices, logger)
    {
    }

    public override CapabilityManifest Manifest =>
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

    protected override DeviceType DeviceType =>
        DeviceType.MotionSensor;

    protected override string DeviceNoun =>
        "motion sensors";
}
