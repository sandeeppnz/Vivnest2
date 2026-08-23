using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Events;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Queues;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Capabilities.MotionSensor;

// Event-driven, not polled - only fires when Detected actually flips, same
// pattern SmartPlugPowerStateChangedHandler already uses for on/off, rather
// than persisting a DeviceEvent on every read (motion sensor reads are
// cheap and frequent, so that would be noisy for no diagnostic gain).
public class MotionSensorStateChangedHandler
    : IEventHandler<MotionSensorStateChangedEvent>
{
    private readonly ILogger<MotionSensorStateChangedHandler> _logger;
    private readonly AgentOptions _agentOptions;
    private readonly MessagingOptions _messagingOptions;
    private readonly IDeviceEventWriter _deviceEventWriter;
    private readonly IQueuePublisher _queuePublisher;

    public MotionSensorStateChangedHandler(
        ILogger<MotionSensorStateChangedHandler> logger,
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
        MotionSensorStateChangedEvent @event,
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
                DeviceType = DeviceType.MotionSensor,
                EventType = DeviceEventTypes.MotionDetected,
                Severity = EventSeverity.Information,
                OccurredAtUtc = @event.ChangedAtUtc,
                Data = new { Detected = @event.Detected },
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
                "Motion state change persisted for {DeviceId}: now {Detected}",
                @event.DeviceId,
                @event.Detected ? "detected" : "clear");

            await _queuePublisher.PublishAsync(
                _messagingOptions.DeviceEventQueue,
                new DeviceEventQueueMessage
                {
                    PartitionKey = entity.PartitionKey,
                    RowKey = entity.RowKey
                },
                cancellationToken);

            _logger.LogInformation(
                "Motion state change queued for cloud processing.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist motion state change for {DeviceId}",
                @event.DeviceId);

            throw;
        }
    }
}
