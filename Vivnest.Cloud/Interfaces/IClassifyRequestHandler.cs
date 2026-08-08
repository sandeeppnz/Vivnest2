using Vivnest.Core.Queues.Models;

namespace Vivnest.Cloud.Interfaces;

public interface IClassifyRequestHandler
{
    Task HandleAsync(
        ClassifyCaptureQueueMessage message,
        CancellationToken cancellationToken = default);
}
