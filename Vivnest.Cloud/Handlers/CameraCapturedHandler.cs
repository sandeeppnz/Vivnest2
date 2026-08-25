using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Notifications;
using Vivnest.Cloud.Options;
using Vivnest.Core.Devices.Camera.Models;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Cloud.Handlers;

public sealed class CameraCapturedHandler : ICameraCapturedHandler
{
    private readonly IDeviceEventReader _deviceEvents;
    private readonly IBlobStorageService _blobStorage;
    private readonly INotificationDispatcher _notifications;
    private readonly IDeviceSnapshotStateReader _snapshotState;
    private readonly SnapshotNotificationOptions _snapshotNotificationOptions;
    private readonly ILogger<CameraCapturedHandler> _logger;

    public CameraCapturedHandler(
        IDeviceEventReader deviceEvents,
        IBlobStorageService blobStorage,
        INotificationDispatcher notifications,
        IDeviceSnapshotStateReader snapshotState,
        IOptions<SnapshotNotificationOptions> snapshotNotificationOptions,
        ILogger<CameraCapturedHandler> logger)
    {
        _deviceEvents = deviceEvents;
        _blobStorage = blobStorage;
        _notifications = notifications;
        _snapshotState = snapshotState;
        _snapshotNotificationOptions = snapshotNotificationOptions.Value;
        _logger = logger;
    }

    public async Task HandleAsync(
        CameraCapturedQueueMessage message,
        CancellationToken cancellationToken = default)
    {
        var entity = await _deviceEvents.GetAsync(
            message.PartitionKey,
            message.RowKey,
            cancellationToken);

        if (entity == null)
        {
            _logger.LogWarning(
                "DeviceEvent not found {PartitionKey}/{RowKey}",
                message.PartitionKey,
                message.RowKey);

            return;
        }

        try
        {
            var data = JsonSerializer.Deserialize<CameraCapturedData>(entity.Payload);

            if (data == null)
                throw new InvalidOperationException(
                    "Unable to deserialize CameraCaptured payload.");

            if (!IsExpectedBlobReference(data))
                throw new InvalidOperationException(
                    $"Refusing to fetch unexpected blob reference '{data.BlobContainer}/{data.BlobName}'.");

            if (await IsDueForNotificationAsync(entity, cancellationToken))
            {
                var image = await _blobStorage.DownloadAsync(
                    data.BlobContainer,
                    data.BlobName,
                    cancellationToken);

                await _notifications.DispatchAsync(
                    new Notification
                    {
                        Type = NotificationTypes.CameraCaptured,
                        Title = $"📷 {entity.DeviceId}",
                        Message = $"{entity.OccurredAtUtc:u}",
                        Images = new[] { image }
                    },
                    cancellationToken);

                await _snapshotState.UpdateLastNotifiedAsync(
                    entity.TenantId,
                    entity.SiteId,
                    entity.AgentId,
                    entity.DeviceId,
                    DateTime.UtcNow,
                    cancellationToken);
            }
            else
            {
                _logger.LogDebug(
                    "Snapshot notification interval not yet elapsed for {DeviceId}; skipping Telegram send.",
                    entity.DeviceId);
            }

            await _deviceEvents.MarkCompletedAsync(
                entity.PartitionKey,
                entity.RowKey,
                cancellationToken);

            _logger.LogInformation(
                "Camera event processed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed processing camera event.");

            await _deviceEvents.MarkFailedAsync(
                entity.PartitionKey,
                entity.RowKey,
                ex.Message,
                cancellationToken);

            throw;
        }
    }

    private async Task<bool> IsDueForNotificationAsync(
        DeviceEventEntity entity,
        CancellationToken cancellationToken)
    {
        if (!_snapshotNotificationOptions.Enabled)
            return false;

        if (_snapshotNotificationOptions.MinInterval <= TimeSpan.Zero)
            return true;

        var state = await _snapshotState.GetAsync(
            entity.TenantId,
            entity.SiteId,
            entity.DeviceId,
            cancellationToken);

        if (state?.LastNotifiedUtc is not { } lastNotifiedUtc)
            return true;

        return DateTime.UtcNow - lastNotifiedUtc >= _snapshotNotificationOptions.MinInterval;
    }

    // Trusts data.BlobContainer the same way it already trusts BlobName/DeviceId/etc
    // elsewhere in this payload - all authored by the Agent via its direct table
    // write access, not attacker-controlled input. Still guards against a malformed
    // BlobName causing a path-traversal read outside the intended blob.
    private bool IsExpectedBlobReference(CameraCapturedData data)
    {
        if (string.IsNullOrWhiteSpace(data.BlobContainer))
            return false;

        if (string.IsNullOrWhiteSpace(data.BlobName))
            return false;

        if (data.BlobName.Contains("..", StringComparison.Ordinal))
            return false;

        return true;
    }
}