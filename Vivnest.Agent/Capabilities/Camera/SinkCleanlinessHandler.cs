using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Interfaces;
using Vivnest.Core.Camera.Stores;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Queues;
using Vivnest.Core.Queues.Models;
using Vivnest.Core.Storage;
using Vivnest.Core.Utils;

namespace Vivnest.Agent.Capabilities.Camera;

// A second handler on CameraCaptureCompletedEvent (multicast dispatch
// already supports this - same shape as MotionTriggerResolverHandler on
// MotionSensorStateChangedEvent) - opt-in per camera via
// DeviceOptions.SinkCleanliness, a no-op for every camera that doesn't set
// it. See ADR-032.
public sealed class SinkCleanlinessHandler : IEventHandler<CameraCaptureCompletedEvent>
{
    private readonly ILogger<SinkCleanlinessHandler> _logger;
    private readonly IDeviceRuntimeStore _deviceRegistry;
    private readonly ICaptureStatusStore _statusStore;
    private readonly ISinkCleanlinessClassifier _classifier;
    private readonly AzureBlobStorageClient _blobStorage;
    private readonly AgentOptions _agentOptions;
    private readonly MessagingOptions _messagingOptions;
    private readonly IDeviceEventWriter _deviceEventWriter;
    private readonly IQueuePublisher _queuePublisher;

    public SinkCleanlinessHandler(
        ILogger<SinkCleanlinessHandler> logger,
        IDeviceRuntimeStore deviceRegistry,
        ICaptureStatusStore statusStore,
        ISinkCleanlinessClassifier classifier,
        AzureBlobStorageClient blobStorage,
        IOptions<AgentOptions> agentOptions,
        IOptions<MessagingOptions> messagingOptions,
        IDeviceEventWriter deviceEventWriter,
        IQueuePublisher queuePublisher)
    {
        _logger = logger;
        _deviceRegistry = deviceRegistry;
        _statusStore = statusStore;
        _classifier = classifier;
        _blobStorage = blobStorage;
        _agentOptions = agentOptions.Value;
        _messagingOptions = messagingOptions.Value;
        _deviceEventWriter = deviceEventWriter;
        _queuePublisher = queuePublisher;
    }

    public async Task HandleAsync(
        CameraCaptureCompletedEvent @event,
        CancellationToken cancellationToken)
    {
        var capture = @event.Result;

        if (capture.BlobName is null || capture.BlobContainer is null)
            return;

        DeviceOptions camera;

        try
        {
            camera = _deviceRegistry.GetDevice(capture.DeviceId, DeviceType.Camera);
        }
        catch (KeyNotFoundException)
        {
            return;
        }

        if (camera.SinkCleanliness is not { Enabled: true } options)
            return;

        try
        {
            var imageBytes = await _blobStorage.DownloadAsync(
                capture.BlobContainer,
                capture.BlobName,
                cancellationToken);

            var result = _classifier.Classify(imageBytes, options);

            if (result is null)
                return;

            var isDirty = !result.IsClean && result.Confidence >= options.ConfidenceThreshold;
            var isClean = !isDirty;

            var runtime = _statusStore.GetOrAdd(capture.DeviceId);

            // LastSinkClean is null only on this process's first observation
            // for this device since restart - same restart-safety baseline
            // MotionSensorMonitorWorker uses, so a restart never reports a
            // phantom transition.
            var isFirstRead = runtime.LastSinkClean is null;
            var changed = !isFirstRead && runtime.LastSinkClean != isClean;

            runtime.LastSinkClean = isClean;

            _logger.LogInformation(
                "Sink cleanliness for {DeviceId}: {State} (confidence={Confidence:F2})",
                capture.DeviceId,
                isDirty ? "dirty" : "clean",
                result.Confidence);

            if (!changed)
                return;

            var deviceEvent = new DeviceEvent
            {
                EventId = Guid.NewGuid(),
                AgentId = _agentOptions.AgentId,
                TenantId = _agentOptions.TenantId,
                SiteId = _agentOptions.SiteId,
                DeviceId = capture.DeviceId,
                DeviceType = DeviceType.Camera,
                EventType = DeviceEventTypes.SinkCleanliness,
                Severity = EventSeverity.Information,
                OccurredAtUtc = capture.CapturedAtUtc,
                Data = new
                {
                    Clean = isClean,
                    result.Confidence,
                    capture.BlobContainer,
                    capture.BlobName,
                },
            };

            var entity = await _deviceEventWriter.SaveAsync(
                deviceEvent,
                cancellationToken);

            if (entity == null)
            {
                _logger.LogWarning(
                    "Unable to persist SinkCleanliness DeviceEvent for {DeviceId}",
                    capture.DeviceId);

                return;
            }

            await _queuePublisher.PublishAsync(
                _messagingOptions.DeviceEventQueue,
                new DeviceEventQueueMessage
                {
                    PartitionKey = entity.PartitionKey,
                    RowKey = entity.RowKey
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Sink cleanliness analysis failed for {DeviceId}",
                capture.DeviceId);

            throw;
        }
    }
}
