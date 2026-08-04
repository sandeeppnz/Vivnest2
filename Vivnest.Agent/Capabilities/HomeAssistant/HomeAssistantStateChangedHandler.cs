using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Interfaces;
using Vivnest.Core.DataStores;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Queues;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Agent.Capabilities.HomeAssistant;

public class HomeAssistantStateChangedHandler
    : IEventHandler<HomeAssistantStateChangedEvent>
{
    private readonly ILogger<HomeAssistantStateChangedHandler> _logger;
    private readonly AgentOptions _agentOptions;
    private readonly MessagingOptions _messagingOptions;
    private readonly IDeviceEventWriter _deviceEventWriter;
    private readonly IQueuePublisher _queuePublisher;
    private readonly IHomeAssistantLivenessTracker _livenessTracker;

    public HomeAssistantStateChangedHandler(
        ILogger<HomeAssistantStateChangedHandler> logger,
        IOptions<AgentOptions> agentOptions,
        IOptions<MessagingOptions> messagingOptions,
        IDeviceEventWriter deviceEventWriter,
        IQueuePublisher queuePublisher,
        IHomeAssistantLivenessTracker livenessTracker)
    {
        _logger = logger;
        _agentOptions = agentOptions.Value;
        _messagingOptions = messagingOptions.Value;
        _deviceEventWriter = deviceEventWriter;
        _queuePublisher = queuePublisher;
        _livenessTracker = livenessTracker;
    }

    public async Task HandleAsync(
        HomeAssistantStateChangedEvent @event,
        CancellationToken cancellationToken)
    {
        // Isolated from the DeviceEvent persistence below - a failure here
        // shouldn't fault the WebSocket read loop over a secondary concern.
        await _livenessTracker.ReportAsync(
            @event.DeviceId,
            @event.DeviceType,
            @event.EntityId,
            @event.State,
            @event.ChangedAtUtc,
            cancellationToken);

        try
        {
            var deviceEvent = new DeviceEvent
            {
                EventId = Guid.NewGuid(),
                AgentId = _agentOptions.AgentId,
                TenantId = _agentOptions.TenantId,
                SiteId = _agentOptions.SiteId,
                DeviceId = @event.DeviceId,
                DeviceType = @event.DeviceType,
                EventType = @event.EventType,
                Severity = EventSeverity.Information,
                OccurredAtUtc = @event.ChangedAtUtc,

                Data = new
                {
                    EntityId = @event.EntityId,
                    State = @event.State,
                    PreviousState = @event.PreviousState,
                    Attributes = @event.Attributes
                }
            };

            var entity = await _deviceEventWriter.SaveAsync(
                deviceEvent,
                cancellationToken);

            if (entity == null)
            {
                _logger.LogWarning(
                    "Unable to persist DeviceEvent for {DeviceId} (entity {EntityId})",
                    @event.DeviceId,
                    @event.EntityId);

                return;
            }

            _logger.LogInformation(
                "Home Assistant state change persisted for {DeviceId} ({EntityId}): {Previous} -> {State}",
                @event.DeviceId,
                @event.EntityId,
                @event.PreviousState,
                @event.State);

            await _queuePublisher.PublishAsync(
                _messagingOptions.DeviceEventQueue,
                new DeviceEventQueueMessage
                {
                    PartitionKey = entity.PartitionKey,
                    RowKey = entity.RowKey
                },
                cancellationToken);

            _logger.LogInformation(
                "Home Assistant state change queued for cloud processing.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist Home Assistant state change for {DeviceId} (entity {EntityId})",
                @event.DeviceId,
                @event.EntityId);

            throw;
        }
    }
}
