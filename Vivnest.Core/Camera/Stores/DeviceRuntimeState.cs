using Vivnest.Core.Enums;

namespace Vivnest.Core.Camera.Stores;

public sealed class DeviceRuntimeState
{
    public bool IsRunning { get; set; }
    public string? LastBlobName { get; set; }
    public string? LastError { get; set; }

    public DateTime? LastCaptureUtc { get; set; }
    public DateTime? LastFailureUtc { get; set; }
    public DateTime? LastStartedUtc { get; set; }
    public DateTime? LastActivityUtc { get; set; }
    public DateTime? LastHeartbeatUtc { get; set; }

    // Last time a motion sensor's battery/signal reading was actually
    // persisted - throttles MotionSensorMonitorWorker's publish against
    // DeviceOptions.BatteryReportInterval, same shape as SnapshotInterval's
    // throttle for cameras/plugs.
    public DateTime? LastBatteryReportUtc { get; set; }

    // Last SinkCleanlinessOptions analysis result, for change detection -
    // null until the first successful analysis this process (restart-safe,
    // same "don't fire on first observation" fix as OfflineDetection/
    // MotionSensorMonitorWorker).
    public bool? LastSinkClean { get; set; }

    // Last status sent via DeviceHeartbeat, for change detection.
    public DeviceHeartbeatStatus? LastReportedStatus { get; set; }

    // Set by CaptureOnTriggerHandler when this device (a camera) gets
    // triggered - CameraCaptureWorker checks these each tick to switch its
    // cadence, and they self-expire (no separate "revert" step) once
    // BurstUntilUtc passes.
    public DateTime? BurstUntilUtc { get; set; }
    public TimeSpan? BurstInterval { get; set; }

    // Carried onto every capture taken while BurstUntilUtc is active (not
    // just the handler's own immediate one), so the dashboard can badge
    // the whole burst, not just its first photo.
    public string? BurstReason { get; set; }

    // Lets CaptureOnTriggerHandler interrupt CameraCaptureWorker's current
    // sleep immediately when a burst starts, instead of waiting for
    // whatever's left of the normal LivenessInterval delay to elapse -
    // without this, "every 30s" wouldn't actually start for up to a full
    // LivenessInterval after the trigger fired.
    public SemaphoreSlim WakeSignal { get; } = new(0, 1);
}