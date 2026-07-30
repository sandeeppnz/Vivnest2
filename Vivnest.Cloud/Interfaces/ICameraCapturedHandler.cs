using Vivnest.Core.Queues.Models;

namespace Vivnest.Cloud.Interfaces;

public interface ICameraCapturedHandler
{
    Task HandleAsync(
        CameraCapturedQueueMessage message,
        CancellationToken cancellationToken = default);
}
