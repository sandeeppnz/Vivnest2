using Vivnest.Core.Queues.Models;

namespace Vivnest.Cloud.Interfaces;

public interface IDeviceEventQueueHandler
{
    Task HandleAsync(
        DeviceEventQueueMessage message,
        CancellationToken cancellationToken = default);
}
