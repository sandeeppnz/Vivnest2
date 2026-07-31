using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Interfaces;
using Vivnest.Agent.Runtime.Events;
using Vivnest.Core.DataStores;
using Vivnest.Core.Options;
using Vivnest.Core.Queues;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Agent.Runtime.EventHandlers;

public class DeviceHeartbeatHandler : IEventHandler<DeviceHeartbeatGeneratedEvent>
{
    private readonly ILogger<DeviceHeartbeatHandler> _logger;
    private readonly IQueuePublisher _queuePublisher;
    private readonly MessagingOptions _messagingOptions;
    private readonly IDeviceHeartbeatStore _deviceHeartbeatStore;

    public DeviceHeartbeatHandler(
        ILogger<DeviceHeartbeatHandler> logger,
        IOptions<MessagingOptions> messagingOptions,
        IDeviceHeartbeatStore deviceHeartbeatStore,
        IQueuePublisher queuePublisher)
    {
        _logger = logger;
        _deviceHeartbeatStore = deviceHeartbeatStore;
        _queuePublisher = queuePublisher;
        _messagingOptions = messagingOptions.Value;
    }

    public async Task HandleAsync(
        DeviceHeartbeatGeneratedEvent @event,
        CancellationToken cancellationToken)
    {
        try
        {
            var entity = await _deviceHeartbeatStore.SaveAsync(
              @event.Heartbeat,
              cancellationToken);

            _logger.LogInformation(
                "Device heartbeat persisted for {DeviceId}",
                @event.Heartbeat.DeviceId);


            await _queuePublisher.PublishAsync(
                _messagingOptions.DeviceHeartbeatQueue,
                new DeviceHeartbeatQueueMessage
                {
                    PartitionKey = entity.PartitionKey,
                    RowKey = entity.RowKey
                },
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
