namespace Vivnest.Core.Options;

/// <summary>
/// This capability's own cadence - how often its throttled "full" action
/// (a capture, a battery report, ...) actually happens, independent of
/// DeviceOptions.LivenessInterval's lightweight tick. Unset/zero Interval
/// means every liveness tick is also a full action. Replaces what used to
/// be separately-named fields per type (SnapshotInterval, BatteryReportInterval)
/// that played the identical role.
/// </summary>
public sealed class ScheduleOptions
{
    public TimeSpan Interval { get; init; }
    public BurstOptions Burst { get; init; } = new();
}

/// <summary>
/// The temporary cadence a capability switches to for Duration after being
/// triggered (see TriggerOptions, CaptureOnTriggerHandler), then reverts to
/// Schedule.Interval once Duration elapses. Only relevant to capabilities
/// that are actually a trigger target.
/// </summary>
public sealed class BurstOptions
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan Duration { get; init; } = TimeSpan.FromMinutes(10);
}
