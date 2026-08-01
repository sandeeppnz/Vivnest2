namespace Vivnest.Cloud.Options;

public class SnapshotNotificationOptions
{
    /// <summary>
    /// Minimum time between Telegram snapshot notifications for the same
    /// device. Zero/unset notifies on every capture.
    /// </summary>
    public TimeSpan MinInterval { get; set; }
}
