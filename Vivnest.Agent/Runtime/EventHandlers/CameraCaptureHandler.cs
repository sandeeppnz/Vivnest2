using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Interfaces;
using Vivnest.Agent.Runtime.Events;
using Vivnest.Core.Camera.Models;
using Vivnest.Core.Camera.Stores;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Queues;
using Vivnest.Core.Queues.Models;
using Vivnest.Core.Utils;

namespace Vivnest.Agent.Runtime.EventHandlers;

public class CameraCaptureHandler : IEventHandler<CameraCaptureCompletedEvent>
{
    private readonly ILogger<CameraCaptureHandler> _logger;
    private readonly IQueuePublisher _queuePublisher;
    private readonly AgentOptions _agentOptions;
    private readonly MessagingOptions _messagingOptions;
    private readonly IDeviceEventWriter _deviceEventWriter;
    private readonly IDeviceRuntimeStore _deviceRuntimeStore;
    private readonly ICaptureStatusStore _statusStore;

    public CameraCaptureHandler(
        ILogger<CameraCaptureHandler> logger,
        IOptions<MessagingOptions> messagingOptions,
        IOptions<AgentOptions> agentOptions,
        IDeviceEventWriter deviceEventWriter,
        IDeviceRuntimeStore deviceRuntimeStore,
        ICaptureStatusStore statusStore,
        IQueuePublisher queuePublisher)
    {
        _logger = logger;
        _messagingOptions = messagingOptions.Value;
        _deviceEventWriter = deviceEventWriter;
        _deviceRuntimeStore = deviceRuntimeStore;
        _statusStore = statusStore;
        _queuePublisher = queuePublisher;
        _agentOptions = agentOptions.Value;
    }

    public async Task HandleAsync(
        CameraCaptureCompletedEvent @event,
        CancellationToken cancellationToken)
    {
        try
        {
            var capture = @event.Result;

            var deviceEvent = new DeviceEvent
            {
                EventId = Guid.NewGuid(),
                AgentId = _agentOptions.AgentId,
                TenantId = _agentOptions.TenantId,
                SiteId = _agentOptions.SiteId,
                DeviceId = capture.DeviceId,
                DeviceType = DeviceType.Camera,
                EventType = DeviceEventTypes.CameraCaptured,
                Severity = EventSeverity.Information,
                OccurredAtUtc = capture.CapturedAtUtc,

                Data = new CameraCapturedData
                {
                    BlobName = capture.BlobName!,
                    BlobContainer = capture.BlobContainer!,
                    CapturedAt = capture.CapturedAtUtc,
                    CaptureDuration = capture.CaptureDuration,
                    UploadDuration = capture.UploadDuration
                }
            };

            var entity = await _deviceEventWriter.SaveAsync(
                    deviceEvent,
                    cancellationToken);

            if (entity == null)
            {
                _logger.LogWarning(
                    "Unable to persist DeviceEvent for {DeviceId}",
                    capture.DeviceId);

                return;
            }

            _logger.LogInformation(
                "Device event persisted for {DeviceId}",
                @event.Result.DeviceId);

            var device = _deviceRuntimeStore.GetDevice(capture.DeviceId);
            var runtime = _statusStore.GetOrAdd(capture.DeviceId);

            var dueForSnapshot =
                device.SnapshotInterval <= TimeSpan.Zero ||
                runtime.LastSnapshotNotifiedUtc == null ||
                DateTime.UtcNow - runtime.LastSnapshotNotifiedUtc.Value >= device.SnapshotInterval;

            if (!dueForSnapshot)
            {
                _logger.LogDebug(
                    "Snapshot interval not yet elapsed for {DeviceId}; skipping notification publish.",
                    capture.DeviceId);

                return;
            }

            await _queuePublisher.PublishAsync(
                _messagingOptions.CameraCapturedQueue,
                new CameraCapturedQueueMessage
                {
                    PartitionKey = entity.PartitionKey,
                    RowKey = entity.RowKey
                },
                cancellationToken);

            runtime.LastSnapshotNotifiedUtc = DateTime.UtcNow;

            _logger.LogInformation(
                "Published CameraCaptured event for Device {DeviceId}",
                @event.Result.DeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish CameraCaptured event for Device {DeviceId}",
                @event.Result.DeviceId);

            throw;
        }
    }
}
