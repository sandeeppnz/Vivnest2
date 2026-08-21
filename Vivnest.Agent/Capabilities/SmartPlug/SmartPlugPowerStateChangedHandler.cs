using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Runtime.Dispatching;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Queues;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Agent.Capabilities.SmartPlug;

// Event-driven, not polled - only fires when IsOn actually flips, same
// pattern PlatformDeviceHeartbeatWorker/OfflineDetection already use for
// online/offline, rather than restating the current state on every read
// the way PowerReading (SmartPlugReadingHandler) already does.
public class SmartPlugPowerStateChangedHandler
    : IEventHandler<SmartPlugPowerStateChangedEvent>
{
    private readonly ILogger<SmartPlugPowerStateChangedHandler> _logger;
    private readonly AgentOptions _agentOptions;
    private readonly MessagingOptions _messagingOptions;
    private readonly IDeviceEventWriter _deviceEventWriter;
    private readonly IQueuePublisher _queuePublisher;

    public SmartPlugPowerStateChangedHandler(
        ILogger<SmartPlugPowerStateChangedHandler> logger,
        IOptions<AgentOptions> agentOptions,
        IOptions<MessagingOptions> messagingOptions,
        IDeviceEventWriter deviceEventWriter,
        IQueuePublisher queuePublisher)
    {
        _logger = logger;
        _agentOptions = agentOptions.Value;
        _messagingOptions = messagingOptions.Value;
        _deviceEventWriter = deviceEventWriter;
        _queuePublisher = queuePublisher;
    }

    public async Task HandleAsync(
        SmartPlugPowerStateChangedEvent @event,
        CancellationToken cancellationToken)
    {
        try
        {
            var deviceEvent = new DeviceEvent
            {
                EventId = Guid.NewGuid(),
                AgentId = _agentOptions.AgentId,
                TenantId = _agentOptions.TenantId,
                SiteId = _agentOptions.SiteId,
                DeviceId = @event.DeviceId,
                DeviceType = DeviceType.SmartPlug,
                EventType = DeviceEventTypes.PowerStateChanged,
                Severity = EventSeverity.Information,
                OccurredAtUtc = @event.ChangedAtUtc,
                Data = new { IsOn = @event.IsOn },
            };

            var entity = await _deviceEventWriter.SaveAsync(
                deviceEvent,
                cancellationToken);

            if (entity == null)
            {
                _logger.LogWarning(
                    "Unable to persist DeviceEvent for {DeviceId}",
                    @event.DeviceId);

                return;
            }

            _logger.LogInformation(
                "Power state change persisted for {DeviceId}: now {IsOn}",
                @event.DeviceId,
                @event.IsOn ? "On" : "Off");

            await _queuePublisher.PublishAsync(
                _messagingOptions.DeviceEventQueue,
                new DeviceEventQueueMessage
                {
                    PartitionKey = entity.PartitionKey,
                    RowKey = entity.RowKey
                },
                cancellationToken);

            _logger.LogInformation(
                "Power state change queued for cloud processing.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist power state change for {DeviceId}",
                @event.DeviceId);

            throw;
        }
    }
}
