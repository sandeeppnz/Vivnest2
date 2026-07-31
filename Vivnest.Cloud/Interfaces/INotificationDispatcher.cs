using Vivnest.Cloud.Notifications;

namespace Vivnest.Cloud.Interfaces;

public interface INotificationDispatcher
{
    Task DispatchAsync(
        Notification notification,
        CancellationToken cancellationToken = default);
}
