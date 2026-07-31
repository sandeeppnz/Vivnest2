using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Notifications;
using Vivnest.Core.Camera.Models;
using Vivnest.Core.Options;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Cloud.Handlers;

public sealed class CameraCapturedHandler : ICameraCapturedHandler
{
    private readonly IDeviceEventReader _deviceEvents;
    private readonly IBlobStorageService _blobStorage;
    private readonly INotificationDispatcher _notifications;
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<CameraCapturedHandler> _logger;

    public CameraCapturedHandler(
        IDeviceEventReader deviceEvents,
        IBlobStorageService blobStorage,
        INotificationDispatcher notifications,
        IOptions<StorageOptions> storageOptions,
        ILogger<CameraCapturedHandler> logger)
    {
        _deviceEvents = deviceEvents;
        _blobStorage = blobStorage;
        _notifications = notifications;
        _storageOptions = storageOptions.Value;
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

    private bool IsExpectedBlobReference(CameraCapturedData data)
    {
        if (!string.Equals(data.BlobContainer, _storageOptions.BlobContainer, StringComparison.Ordinal))
            return false;

        if (string.IsNullOrWhiteSpace(data.BlobName))
            return false;

        if (data.BlobName.Contains("..", StringComparison.Ordinal))
            return false;

        return true;
    }
}