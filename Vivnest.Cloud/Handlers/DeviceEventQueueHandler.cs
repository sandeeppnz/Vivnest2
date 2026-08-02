using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Notifications;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Cloud.Handlers;

// Generic consumer for the "device-events" queue - refetches the entity
// and branches on its EventType, rather than needing a dedicated
// queue/function per event type. CameraCaptureFailedHandler (Agent-side)
// already publishes here too, for camera capture failures; that event
// type isn't handled below yet, deliberately - add a case when someone
// actually needs that notification, same "second real consumer" rule of
// thumb as everywhere else in this codebase.
public sealed class DeviceEventQueueHandler : IDeviceEventQueueHandler
{
    private readonly IDeviceEventReader _deviceEvents;
    private readonly INotificationDispatcher _notifications;
    private readonly ILogger<DeviceEventQueueHandler> _logger;

    public DeviceEventQueueHandler(
        IDeviceEventReader deviceEvents,
        INotificationDispatcher notifications,
        ILogger<DeviceEventQueueHandler> logger)
    {
        _deviceEvents = deviceEvents;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task HandleAsync(
        DeviceEventQueueMessage message,
        CancellationToken cancellationToken = default)
    {
        var entity = await _deviceEvents.GetAsync(
            message.PartitionKey,
            message.RowKey,
            cancellationToken);

        if (entity is null)
        {
            _logger.LogWarning(
                "DeviceEvent not found {PartitionKey}/{RowKey}",
                message.PartitionKey,
                message.RowKey);

            return;
        }

        switch (entity.EventType)
        {
            case DeviceEventTypes.PowerStateChanged:
                await HandlePowerStateChangedAsync(entity, cancellationToken);
                break;

            default:
                _logger.LogDebug(
                    "No handling defined for device event type {EventType}; skipping.",
                    entity.EventType);
                break;
        }
    }

    private async Task HandlePowerStateChangedAsync(
        DeviceEventEntity entity,
        CancellationToken cancellationToken)
    {
        bool isOn;

        try
        {
            using var doc = JsonDocument.Parse(entity.Payload);
            isOn = doc.RootElement.GetProperty("IsOn").GetBoolean();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unable to parse PowerStateChanged payload for {DeviceId}",
                entity.DeviceId);

            return;
        }

        await _notifications.DispatchAsync(
            new Notification
            {
                Type = NotificationTypes.SmartPlugPowerStateChanged,
                Title = isOn
                    ? $"🔌 {entity.DeviceId} turned on"
                    : $"🔌 {entity.DeviceId} turned off",
                Message = $"At {entity.OccurredAtUtc:u}",
                Priority = NotificationPriority.Normal
            },
            cancellationToken);

        _logger.LogInformation(
            "Power state change notification sent for {DeviceId}.",
            entity.DeviceId);
    }
}
