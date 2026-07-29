using Microsoft.Extensions.Logging;
using Vivnest.Agent.Runtime.Dispatching;
using Vivnest.Agent.Runtime.Events;
using Vivnest.Core.Constants;
using Vivnest.Core.Interfaces;

namespace Vivnest.Agent.Runtime.Publishers;

public class CameraCapturePublisher2 : ICapabilityHandler<CameraCapturedEvent>
{
    private readonly ILogger<CameraCapturePublisher2> _logger;
    private readonly IQueuePublisher _queuePublisher;

    public CameraCapturePublisher2(
        ILogger<CameraCapturePublisher2> logger,
        IQueuePublisher queuePublisher)
    {
        _logger = logger;
        _queuePublisher = queuePublisher;
    }

    public async Task HandleAsync(
        CameraCapturedEvent @event,
        CancellationToken cancellationToken)
    {
        try
        {
            await _queuePublisher.PublishAsync(
                QueueNames.CameraCaptured,
                 @event.Result,
                //new CameraCapturedMessage
                //{
                //    PartitionKey = @event.Result.DeviceId,
                //    RowKey = @event.Result.BlobName
                //},
                cancellationToken);


            _logger.LogInformation(
                "Published CameraCaptured event for Device {DeviceId}",
                @event.Result.DeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish CameraCaptured event for Device {DeviceId}",
                @event.Result.DeviceId);

            throw;
        }
    }
}
