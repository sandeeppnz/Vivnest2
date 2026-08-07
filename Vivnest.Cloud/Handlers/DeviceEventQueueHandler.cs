using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Notifications;
using Vivnest.Cloud.Options;
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
    private readonly IBlobStorageService _blobStorage;
    private readonly SinkCleanlinessNotificationOptions _sinkCleanlinessNotificationOptions;
    private readonly ILogger<DeviceEventQueueHandler> _logger;

    public DeviceEventQueueHandler(
        IDeviceEventReader deviceEvents,
        INotificationDispatcher notifications,
        IBlobStorageService blobStorage,
        IOptions<SinkCleanlinessNotificationOptions> sinkCleanlinessNotificationOptions,
        ILogger<DeviceEventQueueHandler> logger)
    {
        _deviceEvents = deviceEvents;
        _notifications = notifications;
        _blobStorage = blobStorage;
        _sinkCleanlinessNotificationOptions = sinkCleanlinessNotificationOptions.Value;
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

            case DeviceEventTypes.MotionDetected:
                await HandleMotionDetectedAsync(entity, cancellationToken);
                break;

            case DeviceEventTypes.SinkCleanliness:
                await HandleSinkCleanlinessAsync(entity, cancellationToken);
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

    private async Task HandleMotionDetectedAsync(
        DeviceEventEntity entity,
        CancellationToken cancellationToken)
    {
        bool detected;

        try
        {
            using var doc = JsonDocument.Parse(entity.Payload);
            detected = doc.RootElement.GetProperty("Detected").GetBoolean();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unable to parse MotionDetected payload for {DeviceId}",
                entity.DeviceId);

            return;
        }

        await _notifications.DispatchAsync(
            new Notification
            {
                Type = NotificationTypes.MotionDetected,
                Title = detected
                    ? $"🏃 Motion detected on {entity.DeviceId}"
                    : $"✅ {entity.DeviceId} clear",
                Message = $"At {entity.OccurredAtUtc:u}",
                Priority = NotificationPriority.Normal
            },
            cancellationToken);

        _logger.LogInformation(
            "Motion detection notification sent for {DeviceId}.",
            entity.DeviceId);
    }

    // Only alerts on the transition to NotClean - the transition back to
    // Clean is still persisted (dashboard history), just silent, per the
    // product decision this feature shipped with (ADR-032). The Agent now
    // queues a SinkCleanliness event on every classification, not just
    // transitions (ADR-034's follow-up, for dashboard Events visibility) -
    // Changed is what still lets this handler alert only once per new
    // "needs cleaning" streak instead of re-alerting on every capture cycle
    // a dirty sink stays dirty.
    private async Task HandleSinkCleanlinessAsync(
        DeviceEventEntity entity,
        CancellationToken cancellationToken)
    {
        bool clean;
        bool changed;
        string? blobContainer;
        string? blobName;

        try
        {
            using var doc = JsonDocument.Parse(entity.Payload);
            var root = doc.RootElement;

            clean = root.GetProperty("Clean").GetBoolean();
            changed = root.TryGetProperty("Changed", out var chg) && chg.GetBoolean();
            blobContainer = root.TryGetProperty("BlobContainer", out var c) ? c.GetString() : null;
            blobName = root.TryGetProperty("BlobName", out var n) ? n.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unable to parse SinkCleanliness payload for {DeviceId}",
                entity.DeviceId);

            return;
        }

        if (clean)
        {
            _logger.LogDebug(
                "Sink cleanliness for {DeviceId} returned to clean; not notifying.",
                entity.DeviceId);

            return;
        }

        if (!changed)
        {
            _logger.LogDebug(
                "Sink cleanliness for {DeviceId} still dirty (no change); not re-alerting.",
                entity.DeviceId);

            return;
        }

        if (!_sinkCleanlinessNotificationOptions.Enabled)
        {
            _logger.LogDebug(
                "SinkCleanlinessNotification disabled; not alerting for {DeviceId}.",
                entity.DeviceId);

            return;
        }

        IReadOnlyList<byte[]>? images = null;

        if (!string.IsNullOrWhiteSpace(blobContainer) &&
            !string.IsNullOrWhiteSpace(blobName) &&
            !blobName.Contains("..", StringComparison.Ordinal))
        {
            var image = await _blobStorage.DownloadAsync(blobContainer, blobName, cancellationToken);
            images = new[] { image };
        }

        await _notifications.DispatchAsync(
            new Notification
            {
                Type = NotificationTypes.SinkCleanliness,
                Title = $"🧽 {entity.DeviceId} needs cleaning",
                Message = $"At {entity.OccurredAtUtc:u}",
                Priority = NotificationPriority.Normal,
                Images = images
            },
            cancellationToken);

        _logger.LogInformation(
            "Sink cleanliness notification sent for {DeviceId}.",
            entity.DeviceId);
    }
}
