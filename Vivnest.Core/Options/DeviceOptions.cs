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
    /// is also due). Also the staleness threshold <c>OfflineDetection</c>
    /// compares the last observed activity against.
    /// </summary>
    public TimeSpan LivenessInterval { get; init; }

    /// <summary>
    /// How often a full capture actually happens (frame grabbed, uploaded,
    /// persisted as a <c>DeviceEvent</c>), independent of
    /// <see cref="LivenessInterval"/>. Unset or zero means every liveness
    /// tick is also a capture, matching prior behavior.
    /// </summary>
    public TimeSpan SnapshotInterval { get; init; }
}
