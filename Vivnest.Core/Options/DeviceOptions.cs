using Vivnest.Core.Enums;

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
    /// Opt-in ML sink-cleanliness classification - see
    /// <see cref="SinkCleanlinessOptions"/> and ADR-032. Null for every
    /// camera except the one it's configured for.
    /// </summary>
    public SinkCleanlinessOptions? SinkCleanliness { get; init; }
}
