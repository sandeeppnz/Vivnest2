using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Cloud.Functions.Queue;

public class ClassifyRequestFunction
{
    private readonly ILogger<ClassifyRequestFunction> _logger;
    private readonly IClassifyRequestHandler _handler;

    public ClassifyRequestFunction(ILogger<ClassifyRequestFunction> logger, IClassifyRequestHandler handler)
    {
        _logger = logger;
        _handler = handler;
    }

    [Function(nameof(ClassifyRequestFunction))]
    public async Task Run(
       [QueueTrigger("classify-requests")]
            ClassifyCaptureQueueMessage message,
       CancellationToken cancellationToken)
    {
        try
        {
            await _handler.HandleAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed relaying classify request for {DeviceId}",
                message.DeviceId);

            throw; // Important: rethrow so Azure Functions retries
        }
    }
}
