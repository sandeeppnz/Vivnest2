using Vivnest.Cloud.Interfaces;

namespace Vivnest.Cloud.Notifications;

public sealed class TelegramNotificationChannel : INotificationChannel
{
    private readonly ITelegramService _telegram;

    public TelegramNotificationChannel(ITelegramService telegram)
    {
        _telegram = telegram;
    }

    public async Task SendAsync(
        Notification notification,
        CancellationToken cancellationToken = default)
    {
        var message = FormatMessage(notification);

        if (notification.Images is { Count: > 0 } images)
        {
            // Telegram's sendPhoto only accepts one photo per call.
            await _telegram.SendPhotoAsync(images[0], message, cancellationToken);
            return;
        }

        await _telegram.SendMessageAsync(message, cancellationToken);
    }

    private static string FormatMessage(Notification notification)
    {
        return string.IsNullOrWhiteSpace(notification.Title)
            ? notification.Message
            : $"{notification.Title}\n{notification.Message}";
    }
}
