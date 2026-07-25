using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Models;
using Vivnest.Core.Models.Camera;

namespace Vivnest.Cloud.Handlers;

public sealed class CameraCapturedHandler : ICameraCapturedHandler
{
    private readonly IDeviceEventRepository _repository;
    private readonly IBlobStorageService _blobStorage;
    private readonly ITelegramService _telegram;
    private readonly ILogger<CameraCapturedHandler> _logger;

    public CameraCapturedHandler(
        IDeviceEventRepository repository,
        IBlobStorageService blobStorage,
        ITelegramService telegram,
        ILogger<CameraCapturedHandler> logger)
    {
        _repository = repository;
        _blobStorage = blobStorage;
        _telegram = telegram;
        _logger = logger;
    }

    public async Task HandleAsync(
        CameraCapturedMessage message,
        CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetAsync(
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

            var caption =
                $"📷 {entity.DeviceId}\n" +
                $"{entity.EventTimestampUtc:u}";

            var image = await _blobStorage.DownloadAsync(
                data.BlobContainer,
                data.BlobName,
                cancellationToken);


            await _telegram.SendPhotoAsync(
                image,
                caption,
                cancellationToken);


            //using var stream = await _blobStorage.OpenReadAsync(
            //    data.BlobContainer,
            //    data.BlobName,
            //    cancellationToken);

            //await _telegram.SendPhotoAsync(
            //    stream,
            //    caption,
            //    cancellationToken);


            await _repository.MarkCompletedAsync(
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

            await _repository.MarkFailedAsync(
                entity.PartitionKey,
                entity.RowKey,
                ex.Message,
                cancellationToken);

            throw;
        }
    }
}