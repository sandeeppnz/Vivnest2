namespace Vivnest.Cloud.Options;

public class SnapshotNotificationOptions
{
    /// <summary>
    /// Whether captured photos are sent to Telegram at all. Independent of
    /// Telegram.Enabled, which also gates device online/offline alerts -
    /// this lets those stay on while muting capture photos specifically.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum time between Telegram snapshot notifications for the same
    /// device. Zero/unset notifies on every capture.
    /// </summary>
    public TimeSpan MinInterval { get; set; }
}
