using Microsoft.Extensions.Logging;
using Vivnest.Core.Capabilities;
using Vivnest.Core.Utils;
using Vivnest.Domain.Devices;

namespace Vivnest.Capabilities.Camera;

public sealed class CameraCapability : DeviceCapabilityBase
{
    // No IRuntimeCapabilityAssignmentStore dependency, deliberately.
    // camera.capture consumes no Agent-level setting: capture cadence is
    // per-device configuration and already flows through
    // DeviceCapability.Settings -> ImageCaptureRuntimeProjector ->
    // ImageCaptureRuntimeAdapter -> DeviceOptions.Schedule.Interval, which
    // lets three cameras on one Agent keep three different schedules. See
    // decision-log.md ADR-097 (5G.11).
    public CameraCapability(
        CameraCaptureWorker worker,
        IDeviceRuntimeStore devices,
        ILogger<CameraCapability> logger)
        : base(worker, devices, logger)
    {
    }

    public override CapabilityManifest Manifest =>
        new()
        {
            Id = "camera.capture",
            Name = "Camera Capture",
            Version = "1.0.0",

            Commands =
            [
                new CapabilityCommandDescriptor(
                    "camera.capture",
                    "1.0")
            ],

            ProducedEvents =
            [
                new CapabilityEventDescriptor(
                    "camera.capture.completed",
                    "1.0"),

                new CapabilityEventDescriptor(
                    "camera.capture.failed",
                    "1.0")
            ],

            ConsumedEvents = [],

            Dependencies = []
        };

    protected override DeviceType DeviceType =>
        DeviceType.Camera;

    protected override string DeviceNoun =>
        "camera devices";

    // Observes its assignment; consumes nothing from it. camera.capture
    // has no Agent-level setting (ADR-101) - capture cadence is per-device
    // and lives on DeviceCapability. Logged so the assignment actually
    // reaching the right capability is visible in a live Agent, not merely
    // asserted in a test.
    protected override void LogStarting(ICapabilityContext context) =>
        Logger.LogInformation(
            "Starting capability {CapabilityId} for agent {AgentId}. " +
            "Assignment={AssignmentCapabilityId}, Enabled={Enabled}, " +
            "SettingsCount={SettingsCount}.",
            Manifest.Id,
            context.AgentId,
            context.Assignment.CapabilityId,
            context.Assignment.Enabled,
            context.Assignment.Settings.Count);
}
