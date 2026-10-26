using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Interfaces;

// One alert to a set of phones. The Android channel id is part of the
// contract because the phone, not the server, decides how loud a channel
// is: "fire-alarm" and "alerts" are created by the app as max-importance
// channels that may bypass Do Not Disturb, "info" is an ordinary one.
public sealed record PushAlert(
    string Title,
    string Body,
    string ChannelId,
    bool HighPriority,
    TimeSpan TimeToLive,
    IReadOnlyDictionary<string, string>? Data = null);

public interface IPushAlertSender
{
    // Returns how many phones Expo accepted the message for. Never throws
    // for a delivery problem: a failure to reach Expo is logged, because
    // the caller (the notification dispatcher) retries the whole
    // notification on an exception, which would re-send it to Telegram.
    Task<int> SendAsync(
        IReadOnlyCollection<PushDeviceEntity> devices,
        PushAlert alert,
        CancellationToken cancellationToken = default);
}
