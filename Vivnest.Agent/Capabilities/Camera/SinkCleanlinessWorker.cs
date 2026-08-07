using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Camera.Stores;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Queues;
using Vivnest.Core.Queues.Models;
using Vivnest.Core.Storage;

namespace Vivnest.Agent.Capabilities.Camera;

// Drains SinkCleanlinessHandler's Channel<T> off the capture pipeline
// entirely (ADR-034, design 1 of 3) - a separate BackgroundService, same
// shape as AgentMetricsWorker (own loop, own try/catch so a hiccup here
// can't touch anything else), so ONNX inference and a blob download never
// delay the next capture tick.
public sealed class SinkCleanlinessWorker : BackgroundService
{
    private readonly ChannelReader<SinkCleanlinessWorkItem> _reader;
    private readonly ICaptureStatusStore _statusStore;
    private readonly ISinkCleanlinessClassifier _classifier;
    private readonly AzureBlobStorageClient _blobStorage;
    private readonly AgentOptions _agentOptions;
    private readonly MessagingOptions _messagingOptions;
    private readonly IDeviceEventWriter _deviceEventWriter;
    private readonly IQueuePublisher _queuePublisher;
    private readonly ILogger<SinkCleanlinessWorker> _logger;

    public SinkCleanlinessWorker(
        ChannelReader<SinkCleanlinessWorkItem> reader,
        ICaptureStatusStore statusStore,
        ISinkCleanlinessClassifier classifier,
        AzureBlobStorageClient blobStorage,
        IOptions<AgentOptions> agentOptions,
        IOptions<MessagingOptions> messagingOptions,
        IDeviceEventWriter deviceEventWriter,
        IQueuePublisher queuePublisher,
        ILogger<SinkCleanlinessWorker> logger)
    {
        _reader = reader;
        _statusStore = statusStore;
        _classifier = classifier;
        _blobStorage = blobStorage;
        _agentOptions = agentOptions.Value;
        _messagingOptions = messagingOptions.Value;
        _deviceEventWriter = deviceEventWriter;
        _queuePublisher = queuePublisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(item, stoppingToken);
            }
            catch (Exception ex)
            {
                // Deliberately not rethrown - same reasoning as ADR-033's
                // follow-up fix: a sink-cleanliness hiccup must never
                // surface as anything else failing. This loop has to
                // survive it too, or every later capture's classification
                // would silently stop along with it.
                _logger.LogError(
                    ex,
                    "Sink cleanliness analysis failed for {DeviceId}",
                    item.DeviceId);
            }
        }
    }

    private async Task ProcessAsync(SinkCleanlinessWorkItem item, CancellationToken cancellationToken)
    {
        var imageBytes = await _blobStorage.DownloadAsync(
            item.BlobContainer,
            item.BlobName,
            cancellationToken);

        var result = _classifier.Classify(imageBytes, item.Options);

        if (result is null)
            return;

        var isDirty = !result.IsClean && result.Confidence >= item.Options.ConfidenceThreshold;
        var isClean = !isDirty;

        var runtime = _statusStore.GetOrAdd(item.DeviceId);

        // LastSinkClean is null only on this process's first observation
        // for this device since restart - same restart-safety baseline
        // MotionSensorMonitorWorker uses, so a restart never reports a
        // phantom transition.
        var isFirstRead = runtime.LastSinkClean is null;
        var changed = !isFirstRead && runtime.LastSinkClean != isClean;

        runtime.LastSinkClean = isClean;

        _logger.LogInformation(
            "Sink cleanliness for {DeviceId}: {State} (confidence={Confidence:F2})",
            item.DeviceId,
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
            DeviceId = item.DeviceId,
            DeviceType = DeviceType.Camera,
            EventType = DeviceEventTypes.SinkCleanliness,
            Severity = EventSeverity.Information,
            OccurredAtUtc = item.CapturedAtUtc,
            Data = new
            {
                Clean = isClean,
                result.Confidence,
                item.BlobContainer,
                item.BlobName,
            },
        };

        var entity = await _deviceEventWriter.SaveAsync(
            deviceEvent,
            cancellationToken);

        if (entity == null)
        {
            _logger.LogWarning(
                "Unable to persist SinkCleanliness DeviceEvent for {DeviceId}",
                item.DeviceId);

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
}
