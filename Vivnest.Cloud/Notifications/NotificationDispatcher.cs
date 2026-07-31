using Microsoft.Extensions.Logging;
using Vivnest.Cloud.Interfaces;

namespace Vivnest.Cloud.Notifications;

public sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        IEnumerable<INotificationChannel> channels,
        ILogger<NotificationDispatcher> logger)
    {
        _channels = channels;
        _logger = logger;
    }

    public async Task DispatchAsync(
        Notification notification,
        CancellationToken cancellationToken = default)
    {
        List<Exception>? exceptions = null;

        foreach (var channel in _channels)
        {
            try
            {
                await channel.SendAsync(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to send notification {Type} via {Channel}.",
                    notification.Type,
                    channel.GetType().Name);

                (exceptions ??= new List<Exception>()).Add(ex);
            }
        }

        if (exceptions is { Count: > 0 })
        {
            throw new AggregateException(
                $"One or more channels failed to send notification {notification.Type}.",
                exceptions);
        }
    }
}
