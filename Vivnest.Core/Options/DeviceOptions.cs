using Vivnest.Domain.Devices;

namespace Vivnest.Core.Options;

public class DeviceOptions
{
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public DeviceType Type { get; set; }
    public bool Enabled { get; set; }

    /// <summary>
    /// Purely descriptive - never read by any capability's worker to decide
    /// behavior. Brand/Model/Firmware are a fallback for device types that
    /// can't self-report them (Camera today); where a capability already
    /// reports one as a reading (e.g. SmartPlugState.Brand), the reading
    /// takes precedence for display and this is only shown if that's absent.
    /// </summary>
    public string Location { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Model { get; set; } = "";
    public string Firmware { get; set; } = "";

    /// <summary>
    /// The DeviceId this device is reached through, if any (e.g. a motion
    /// sensor's Tapo hub) - not this device's own connection detail, that's
    /// what <see cref="DeviceSettings.ChildDeviceId"/> is for. Used
    /// Cloud-side to gate this device's status on its parent's: if the hub
    /// is unreachable, nothing this device last reported can be trusted
    /// either. Empty for devices with no parent.
    /// </summary>
    public string ParentDeviceId { get; set; } = "";

    public DeviceSettings Settings { get; set; } = new();

    /// <summary>
    /// Which Low-type agent owns/loads this device - the physical agent
    /// process holding the device connection (decision-log.md ADR-036,
    /// renamed from CaptureAgentId in ADR-045). Distinct from the
    /// AI-capability ExecutingAgentId on
    /// <see cref="SinkCleanlinessRoiOptions"/>/<see cref="ObjectDetectionRoiOptions"/>,
    /// which identifies a different kind of ownership - which High-type
    /// agent executes a classification, never which agent talks to the
    /// hardware. Read only at startup (Program.cs, to filter the shared
    /// device-config container down to "devices this agent owns") - no
    /// runtime capability reads it after that.
    /// </summary>
    public string OwningAgentId { get; set; } = "";

    /// <summary>
    /// How often the device is checked for liveness (a lightweight
    /// reachability probe, or a full action when <see cref="Schedule"/>'s
    /// own interval is also due). Also the base unit <c>OfflineDetection</c>
    /// multiplies by <see cref="WarningMultiplier"/> to get the staleness
    /// threshold it compares the last observed activity against.
    /// </summary>
    public TimeSpan LivenessInterval { get; init; }

    /// <summary>
    /// How many missed <see cref="LivenessInterval"/>s before
    /// <c>OfflineDetection</c> marks this device Warning - buffer against a
    /// single delayed probe causing a false alert. Default 3 mirrors the
    /// buffered default used elsewhere (<c>HealthMonitorOptions.AgentStaleMultiplier</c>).
    /// Set to 1 (no buffer) for devices where fast detection matters more
    /// than avoiding an occasional false positive.
    /// </summary>
    public double WarningMultiplier { get; init; } = 3.0;

    /// <summary>
    /// This capability's own cadence - see <see cref="ScheduleOptions"/>.
    /// </summary>
    public ScheduleOptions Schedule { get; init; } = new();

    /// <summary>
    /// What this device triggers when it fires an event worth reacting to
    /// - see <see cref="TriggerOptions"/>.
    /// </summary>
    public TriggerOptions Trigger { get; init; } = new();

    /// <summary>
    /// Opt-in ML sink-cleanliness classification - camera-specific facts
    /// only (whether it's on, and where the ROI is). See
    /// <see cref="SinkCleanlinessRoiOptions"/>, ADR-032, and ADR-035's
    /// follow-up for why model behavior lives on the High-type agent instead.
    /// Null for every camera except the one it's configured for.
    /// </summary>
    public SinkCleanlinessRoiOptions? SinkCleanliness { get; init; }

    /// <summary>
    /// Opt-in ML object detection (unusual-object flagging) - camera-specific
    /// facts only. See <see cref="ObjectDetectionRoiOptions"/>, ADR-034's
    /// follow-up, ADR-035's follow-up for why model behavior lives on the
    /// High-type agent instead, and ADR-036 for why this no longer gates
    /// SinkCleanliness (routes to its own, possibly different, High-type
    /// agent now). Null for every camera except the one it's configured for.
    /// </summary>
    public ObjectDetectionRoiOptions? ObjectDetection { get; init; }

    /// <summary>
    /// Hardware present on this device, for the dashboard's Source sensors
    /// view (decision-log.md ADR-040). Hand-authored per device, since
    /// accessibility is a fact about this specific device/model, not a
    /// property of <see cref="Type"/>. Empty by default - backward
    /// compatible with every device-config blob that predates this field;
    /// an empty list just means an empty Source sensors section until
    /// hand-authored, not an error.
    /// </summary>
    public IReadOnlyList<SensorOptions> Sensors { get; init; } = [];

    /// <summary>
    /// When this device's configuration was last published by Admin
    /// (decision-log.md ADR-065) - set only for devices whose
    /// device-config/*.json is the new capabilities[]-shaped document
    /// written by IDeviceRuntimeConfigurationPublisher; null for a
    /// legacy-shape device that has never been published through that
    /// pipeline, which is itself informative. Reported via
    /// DeviceHeartbeat.ConfigurationPublishedUtc - never read by any
    /// worker to decide behavior.
    /// </summary>
    public DateTime? ConfigurationPublishedUtc { get; init; }

    /// <summary>
    /// Decision-log.md ADR-069 - set only when this device was loaded via
    /// the new versioned manifest path (device-config/{id}/current.json +
    /// versions/{n}.json), not the legacy flat blob. Null means either
    /// "never published through the new pipeline" or "loaded via the
    /// legacy flat blob" - both report as null identically, since neither
    /// has a real version number to report.
    /// </summary>
    public int? ConfigurationVersion { get; init; }

    public string? ConfigurationHash { get; init; }
}
