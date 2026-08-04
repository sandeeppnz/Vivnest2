using Vivnest.Core.Enums;

namespace Vivnest.Core.Options;

public class DeviceOptions
{
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public DeviceType Type { get; set; }
    public bool Enabled { get; set; }
    public DeviceSettings Settings { get; set; } = new();

    /// <summary>
    /// How often the device is checked for liveness (a lightweight
    /// reachability probe, or a full capture when <see cref="SnapshotInterval"/>
    /// is also due). Also the base unit <c>OfflineDetection</c> multiplies
    /// by <see cref="WarningMultiplier"/> to get the staleness threshold it
    /// compares the last observed activity against.
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
    /// How often a full capture actually happens (frame grabbed, uploaded,
    /// persisted as a <c>DeviceEvent</c>), independent of
    /// <see cref="LivenessInterval"/>. Unset or zero means every liveness
    /// tick is also a capture, matching prior behavior.
    /// </summary>
    public TimeSpan SnapshotInterval { get; init; }

    /// <summary>
    /// Other devices this device triggers when it fires an event worth
    /// reacting to (currently: a motion sensor going <c>Detected</c>).
    /// Resolved by <c>MotionTriggerResolverHandler</c>, empty for devices
    /// that don't trigger anything.
    /// </summary>
    public string[] TriggersDeviceIds { get; init; } = [];

    /// <summary>
    /// Capture cadence a camera switches to for <see cref="MotionBurstDuration"/>
    /// after being triggered (e.g. by a motion sensor's
    /// <see cref="TriggersDeviceIds"/>) - see <c>CaptureOnTriggerHandler</c>.
    /// Only relevant to cameras that are actually a trigger target.
    /// </summary>
    public TimeSpan MotionBurstInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a motion-triggered burst lasts before the camera reverts to
    /// its normal <see cref="SnapshotInterval"/>/<see cref="LivenessInterval"/>
    /// cadence - see <c>CaptureOnTriggerHandler</c>/<c>CameraCaptureWorker</c>.
    /// </summary>
    public TimeSpan MotionBurstDuration { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How often a motion sensor's battery/signal reading actually gets
    /// persisted as a <c>DeviceEvent</c>, independent of
    /// <see cref="LivenessInterval"/> - same throttle shape as
    /// <see cref="SnapshotInterval"/> for cameras, just for a field that's
    /// already free on every liveness read instead of requiring a separate
    /// capture. Defaults to 2 hours since battery status changes slowly;
    /// zero/unset reports on every liveness tick instead.
    /// </summary>
    public TimeSpan BatteryReportInterval { get; init; } = TimeSpan.FromHours(2);
}
