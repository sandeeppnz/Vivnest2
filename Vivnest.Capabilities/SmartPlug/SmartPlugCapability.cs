using Microsoft.Extensions.Logging;
using Vivnest.Core.Capabilities;
using Vivnest.Core.Utils;
using Vivnest.Domain.Devices;

namespace Vivnest.Capabilities.SmartPlug;

public sealed class SmartPlugCapability : DeviceCapabilityBase
{
    public SmartPlugCapability(
        SmartPlugMonitorWorker worker,
        IDeviceRuntimeStore devices,
        ILogger<SmartPlugCapability> logger)
        : base(worker, devices, logger)
    {
    }

    public override CapabilityManifest Manifest =>
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

    protected override DeviceType DeviceType =>
        DeviceType.SmartPlug;

    protected override string DeviceNoun =>
        "smart plugs";
}
