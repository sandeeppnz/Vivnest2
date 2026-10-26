using Microsoft.Extensions.Logging;
using Vivnest.Core.Capabilities;
using Vivnest.Core.Utils;
using Vivnest.Domain.Devices;

namespace Vivnest.Capabilities.Audio;

public sealed class AudioAlarmCapability : DeviceCapabilityBase
{
    // Cadence, clip length and cooldown are per-device and arrive on
    // DeviceOptions.AudioAlarm (AudioAlarmRuntimeAdapter), so - like
    // camera.capture (ADR-097) - this takes no Agent-level setting and no
    // assignment store.
    public AudioAlarmCapability(
        AudioAlarmWorker worker,
        IDeviceRuntimeStore devices,
        ILogger<AudioAlarmCapability> logger)
        : base(worker, devices, logger)
    {
    }

    public override CapabilityManifest Manifest =>
        new()
        {
            Id = "audio.alarm",
            Name = "Audio Alarm Detection",
            Version = "1.0.0",

            Commands = [],

            ProducedEvents =
            [
                new CapabilityEventDescriptor(
                    "audio.alarm.detected",
                    "1.0")
            ],

            ConsumedEvents = [],

            Dependencies = []
        };

    protected override DeviceType DeviceType =>
        DeviceType.Camera;

    protected override string DeviceNoun =>
        "camera devices";
}
