using Microsoft.Extensions.Logging;
using Vivnest.Cloud.Interfaces;

namespace Vivnest.Cloud.Notifications;

// The loud, separate path for the alerts that must not be missed (ADR-129).
// Only the types below are pushed - motion, power and sink alerts stay on
// Telegram, because a phone that rings through Do Not Disturb for a motion
// event is a phone whose owner turns the app off.
public sealed class ExpoPushNotificationChannel : INotificationChannel
{
    private static readonly TimeSpan AlertTimeToLive = TimeSpan.FromHours(1);
    private static readonly TimeSpan InfoTimeToLive = TimeSpan.FromHours(6);

    public const string FireAlarmChannel = "fire-alarm";
    public const string AlertsChannel = "alerts";
    public const string InfoChannel = "info";

    // type -> (android channel, loud)
    private static readonly Dictionary<string, (string Channel, bool Loud)> Routes = new(StringComparer.Ordinal)
    {
        [NotificationTypes.FireAlarmDetected] = (FireAlarmChannel, true),
        [NotificationTypes.DeviceOffline] = (AlertsChannel, true),
        [NotificationTypes.AgentOffline] = (AlertsChannel, true),
        [NotificationTypes.DeviceRecovered] = (InfoChannel, false),
        [NotificationTypes.AgentRecovered] = (InfoChannel, false),
    };

    private readonly IPushDeviceStore _devices;
    private readonly IPushAlertSender _sender;
    private readonly ILogger<ExpoPushNotificationChannel> _logger;

    public ExpoPushNotificationChannel(
        IPushDeviceStore devices,
        IPushAlertSender sender,
        ILogger<ExpoPushNotificationChannel> logger)
    {
        _devices = devices;
        _sender = sender;
        _logger = logger;
    }

    public static bool IsPushed(string notificationType) => Routes.ContainsKey(notificationType);

    public async Task SendAsync(
        Notification notification,
        CancellationToken cancellationToken = default)
    {
        if (!Routes.TryGetValue(notification.Type, out var route))
            return;

        // A store failure must not escape: the dispatcher would retry the
        // whole notification and re-send it to Telegram.
        IReadOnlyList<Entities.PushDeviceEntity> all;

        try
        {
            all = await _devices.ListAllAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Could not read the registered phones; {Type} was not pushed.", notification.Type);
            return;
        }

        var phones = all.Where(d => d.Enabled).ToList();

        if (phones.Count == 0)
            return;

        await _sender.SendAsync(
            phones,
            new PushAlert(
                Title: string.IsNullOrWhiteSpace(notification.Title) ? notification.Type : notification.Title,
                Body: notification.Message,
                ChannelId: route.Channel,
                HighPriority: route.Loud,
                TimeToLive: route.Loud ? AlertTimeToLive : InfoTimeToLive,
                Data: new Dictionary<string, string> { ["type"] = notification.Type }),
            cancellationToken);
    }
}
