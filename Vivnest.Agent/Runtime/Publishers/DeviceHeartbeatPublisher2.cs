using Microsoft.Extensions.Logging;
using Vivnest.Agent.Runtime.Dispatching;
using Vivnest.Agent.Runtime.Events;
using Vivnest.Core.Constants;
using Vivnest.Core.Interfaces;

namespace Vivnest.Agent.Runtime.Publishers;

public class DeviceHeartbeatPublisher2 : ICapabilityHandler<DeviceHeartbeatReceivedEvent>
{
    private readonly ILogger<DeviceHeartbeatPublisher2> _logger;
    private readonly IQueuePublisher _queuePublisher;

    public DeviceHeartbeatPublisher2(
        ILogger<DeviceHeartbeatPublisher2> logger,
        IQueuePublisher queuePublisher)
    {
        _logger = logger;
        _queuePublisher = queuePublisher;
    }

    public async Task HandleAsync(
        DeviceHeartbeatReceivedEvent @event,
        CancellationToken cancellationToken)
    {
        try
        {
            await _queuePublisher.PublishAsync(
                QueueNames.DeviceHeartbeat,
                @event.Heartbeat,
                cancellationToken);

            _logger.LogInformation(
                "Published DeviceHeartbeat for {DeviceId}",
                @event.Heartbeat.DeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed publishing DeviceHeartbeat for {DeviceId}",
                @event.Heartbeat.DeviceId);

            throw;
        }
    }
}
