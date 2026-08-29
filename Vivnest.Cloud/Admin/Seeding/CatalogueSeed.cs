using Vivnest.Cloud.Api.Dtos;
using Vivnest.Domain.Devices;

namespace Vivnest.Cloud.Admin.Seeding;

// One catalogue seed entry: the identity pair the runtime binds on
// (Name for the config projectors, Key for the agent-side capability
// adapters) plus the settings schema the projector validates.
public sealed record CapabilitySeed(
    string Name,
    string Key,
    string Type,
    IReadOnlyList<CapabilityConfigurationFieldDto> Schema,
    IReadOnlyDictionary<string, string> Defaults,
    IReadOnlyList<DeviceType> CompatibleDeviceTypes);

// The canonical capability catalogue, IN CODE (ADR-119). Every value
// here restates a constant the runtime already compiled in:
//
//   Name   <- ICapabilityRuntimeProjector.CapabilityName (projection
//             binds case/whitespace-insensitively on it)
//   Key    <- the agent-side adapter Id (CameraCapability's
//             "camera.capture", MotionSensorCapability's
//             "motion.sensor"); AgentCapabilityAssignmentFactory drops
//             any config entry whose key matches no registered adapter,
//             SILENTLY - which is exactly how a hand-typed catalogue
//             failed in practice (2026-08-29's factory-reset rebuild:
//             a keyless "Image Capture" published cleanly and then did
//             nothing).
//   Schema <- each projector's own setting-key constants.
//
// CatalogueSeedServiceTests pins this list against the real projector
// set, so adding a projector without a seed entry fails the build's
// tests instead of failing silently at a customer's first setup.
public static class CatalogueSeed
{
    private static CapabilityConfigurationFieldDto Number(string name, bool required) =>
        new(name, "Number", required, 1, null, null, null);

    private static CapabilityConfigurationFieldDto Text(string name) =>
        new(name, "String", false, null, null, null, null);

    public static readonly IReadOnlyList<CapabilitySeed> Capabilities =
    [
        new CapabilitySeed(
            "Image Capture",
            "camera.capture",
            "Device",
            [
                Number("ScheduleIntervalSeconds", required: true),
                Number("BurstIntervalSeconds", required: true),
                Number("BurstDurationSeconds", required: true),
            ],
            new Dictionary<string, string>
            {
                ["ScheduleIntervalSeconds"] = "900",
                ["BurstIntervalSeconds"] = "30",
                ["BurstDurationSeconds"] = "120",
            },
            [DeviceType.Camera]),

        new CapabilitySeed(
            "Motion Detection",
            "motion.sensor",
            "Device",
            [Number("BatteryReportIntervalMinutes", required: false)],
            new Dictionary<string, string>(),
            [DeviceType.MotionSensor]),

        // The two AI capabilities execute on the High-type agent via the
        // projected AiClassification section, not via a capability-host
        // adapter - their keys are identity only, chosen in the same
        // dotted style.
        new CapabilitySeed(
            "Object Detection",
            "object.detection",
            "Device",
            [
                Number("RoiLeft", required: true),
                Number("RoiTop", required: true),
                Number("RoiRight", required: true),
                Number("RoiBottom", required: true),
                Text("ModelPath"),
                Text("ConfidenceThreshold"),
                Text("ExpectedClasses"),
            ],
            new Dictionary<string, string>(),
            [DeviceType.Camera]),

        new CapabilitySeed(
            "Sink Cleanliness",
            "sink.cleanliness",
            "Device",
            [
                Number("RoiLeft", required: true),
                Number("RoiTop", required: true),
                Number("RoiRight", required: true),
                Number("RoiBottom", required: true),
                Text("ModelPath"),
                Text("ConfidenceThreshold"),
            ],
            new Dictionary<string, string>(),
            [DeviceType.Camera]),
    ];
}
