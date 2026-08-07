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
    // DeviceOptions.Schedule.Interval (with its own 2-hour fallback when
    // unset), same throttle shape Camera/SmartPlug get from Schedule.Interval
    // directly.
    public DateTime? LastBatteryReportUtc { get; set; }

    // Last status sent via DeviceHeartbeat, for change detection.
    public DeviceHeartbeatStatus? LastReportedStatus { get; set; }

    // Last sink-cleanliness classification for this camera (ADR-032), null
    // only on this process's first observation since restart - the same
    // restart-safety baseline pattern MotionSensorMonitorWorker uses for
    // LastReportedStatus, so a restart never reports a phantom transition.
    public bool? LastSinkClean { get; set; }

    // Last time object detection found a person overlapping this camera's
    // ROI (ADR-034's follow-up) - carried onto the next SinkCleanliness
    // event as timing-only context (no identity), and used to skip
    // classification entirely for the capture the person was found in.
    public DateTime? LastPersonSeenUtc { get; set; }

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

    // Which burst SinkCleanlinessHandler has already let through its
    // first-capture check, identified by that burst's own BurstUntilUtc
    // (unique per trigger, since a new burst always sets a later value) -
    // TriggerReason alone can't tell first from Nth burst capture, since
    // it's the same BurstReason string for the whole burst. Self-cleaning:
    // the next real burst gets a new BurstUntilUtc, so this never needs an
    // explicit reset.
    public DateTime? LastAnalyzedBurstUntilUtc { get; set; }

    // Lets CaptureOnTriggerHandler interrupt CameraCaptureWorker's current
    // sleep immediately when a burst starts, instead of waiting for
    // whatever's left of the normal LivenessInterval delay to elapse -
    // without this, "every 30s" wouldn't actually start for up to a full
    // LivenessInterval after the trigger fired.
    public SemaphoreSlim WakeSignal { get; } = new(0, 1);
}