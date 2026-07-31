using Vivnest.Cloud.Notifications;

namespace Vivnest.Cloud.Interfaces;

public interface INotificationChannel
{
    Task SendAsync(
        Notification notification,
        CancellationToken cancellationToken = default);
}
