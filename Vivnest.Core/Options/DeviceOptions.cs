using Vivnest.Core.Enums;

namespace Vivnest.Core.Options;

public class DeviceOptions
{
    public string DeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public DeviceType Type { get; set; }
    public bool Enabled { get; set; }
    public DeviceSettings Settings { get; set; } = new();
    public TimeSpan ActivityInterval { get; init; }

    /// <summary>
    /// How often a capture should be forwarded as a user-facing snapshot
    /// notification (e.g. Telegram photo), independent of how often
    /// <see cref="ActivityInterval"/> captures a frame for liveness. Unset
    /// or zero means every capture is notified, matching prior behavior.
    /// </summary>
    public TimeSpan SnapshotInterval { get; init; }
}
