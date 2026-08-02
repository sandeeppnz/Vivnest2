using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Cloud.Functions.Queue;

public class DeviceEventQueueFunction
{
    private readonly ILogger<DeviceEventQueueFunction> _logger;
    private readonly IDeviceEventQueueHandler _handler;

    public DeviceEventQueueFunction(
        ILogger<DeviceEventQueueFunction> logger,
        IDeviceEventQueueHandler handler)
    {
        _logger = logger;
        _handler = handler;
    }

    [Function(nameof(DeviceEventQueueFunction))]
    public async Task Run(
        [QueueTrigger("device-events")]
            DeviceEventQueueMessage message,
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
                "Failed processing device event {PartitionKey}/{RowKey}",
                message.PartitionKey,
                message.RowKey);

            throw; // Important: rethrow so Azure Functions retries
        }
    }
}
