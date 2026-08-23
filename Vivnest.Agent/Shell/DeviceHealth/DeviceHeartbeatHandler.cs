using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Events;
using Vivnest.Core.DataStores;
using Vivnest.Core.Options;
using Vivnest.Core.Queues;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Agent.Shell.DeviceHealth;

public class DeviceHeartbeatHandler : IEventHandler<DeviceHeartbeatGeneratedEvent>
{
    private readonly ILogger<DeviceHeartbeatHandler> _logger;
    private readonly IQueuePublisher _queuePublisher;
    private readonly MessagingOptions _messagingOptions;
    private readonly IDeviceHeartbeatWriter _deviceHeartbeatWriter;

    public DeviceHeartbeatHandler(
        ILogger<DeviceHeartbeatHandler> logger,
        IOptions<MessagingOptions> messagingOptions,
        IDeviceHeartbeatWriter deviceHeartbeatWriter,
        IQueuePublisher queuePublisher)
    {
        _logger = logger;
        _deviceHeartbeatWriter = deviceHeartbeatWriter;
        _queuePublisher = queuePublisher;
        _messagingOptions = messagingOptions.Value;
    }

    public async Task HandleAsync(
        DeviceHeartbeatGeneratedEvent @event,
        CancellationToken cancellationToken)
    {
        try
        {
            var entity = await _deviceHeartbeatWriter.SaveAsync(
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
        catch
        {
            // See AgentHeartbeatHandler - DeviceHeartbeatWorker's catch
            // logs this, and it names the DeviceId too, so logging here
            // as well produced two ErrorLogged AgentEvents per failure.
            //
            // HomeAssistantLivenessTracker also calls this handler
            // directly and swallows-and-logs on its own, so that path
            // still reports.
            throw;
        }
    }
}
