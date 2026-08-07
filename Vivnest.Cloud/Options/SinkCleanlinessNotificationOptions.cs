namespace Vivnest.Cloud.Options;

public class SinkCleanlinessNotificationOptions
{
    /// <summary>
    /// Whether a "needs cleaning" alert is sent to Telegram at all.
    /// Independent of Telegram.Enabled (which also gates every other
    /// alert) and of DeviceOptions.SinkCleanliness.Enabled (which controls
    /// whether the Agent classifies at all) - this lets classification and
    /// dashboard history keep working while muting just this alert, same
    /// shape as SnapshotNotificationOptions.Enabled for capture photos.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
