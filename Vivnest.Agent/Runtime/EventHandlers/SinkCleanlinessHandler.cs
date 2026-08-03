using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Interfaces;
using Vivnest.Agent.Runtime.Events;
using Vivnest.Agent.Services;
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

namespace Vivnest.Agent.Runtime.EventHandlers;

// A second handler on CameraCaptureCompletedEvent (multicast dispatch
// already supports this - same shape as MotionTriggerResolverHandler on
// MotionSensorStateChangedEvent) - opt-in per camera via
// DeviceOptions.SinkCleanliness, a no-op for every camera that doesn't set
// it. Unlike motion/plug state changes, there's no upstream worker loop
// that already decided "did this change" - the analysis itself only makes
// sense once a capture has actually succeeded and uploaded, so it happens
// here rather than in CameraCaptureWorker's tight loop.
public class SinkCleanlinessHandler : IEventHandler<CameraCaptureCompletedEvent>
{
    private readonly ILogger<SinkCleanlinessHandler> _logger;
    private readonly IDeviceRuntimeStore _deviceRegistry;
    private readonly ICaptureStatusStore _statusStore;
    private readonly ISinkCleanlinessAnalyzer _analyzer;
    private readonly AzureBlobStorageClient _blobStorage;
    private readonly AgentOptions _agentOptions;
    private readonly MessagingOptions _messagingOptions;
    private readonly IDeviceEventWriter _deviceEventWriter;
    private readonly IQueuePublisher _queuePublisher;

    public SinkCleanlinessHandler(
        ILogger<SinkCleanlinessHandler> logger,
        IDeviceRuntimeStore deviceRegistry,
        ICaptureStatusStore statusStore,
        ISinkCleanlinessAnalyzer analyzer,
        AzureBlobStorageClient blobStorage,
        IOptions<AgentOptions> agentOptions,
        IOptions<MessagingOptions> messagingOptions,
        IDeviceEventWriter deviceEventWriter,
        IQueuePublisher queuePublisher)
    {
        _logger = logger;
        _deviceRegistry = deviceRegistry;
        _statusStore = statusStore;
        _analyzer = analyzer;
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

        if (!capture.Success || capture.BlobName is null || capture.BlobContainer is null)
            return;

        DeviceOptions device;

        try
        {
            device = _deviceRegistry.GetDevice(capture.DeviceId);
        }
        catch (KeyNotFoundException)
        {
            return;
        }

        if (device.SinkCleanliness is not { Enabled: true } options)
            return;

        try
        {
            var imageBytes = await _blobStorage.DownloadAsync(
                capture.BlobContainer,
                capture.BlobName,
                cancellationToken);

            var result = _analyzer.Analyze(imageBytes, options);

            var runtime = _statusStore.GetOrAdd(capture.DeviceId);

            // First observation this process establishes the baseline
            // silently - same restart-safety fix as
            // MotionSensorMonitorWorker (decision-log.md's ADR-019
            // follow-up); without it, every agent restart would report a
            // phantom transition.
            var isFirstRead = runtime.LastSinkClean is null;
            var changed = !isFirstRead && result.Clean != runtime.LastSinkClean;

            runtime.LastSinkClean = result.Clean;

            _logger.LogInformation(
                "Sink cleanliness for {DeviceId}: {Clean} (score={Score:F2})",
                capture.DeviceId,
                result.Clean,
                result.Score);

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
                    result.Clean,
                    result.Score,
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
