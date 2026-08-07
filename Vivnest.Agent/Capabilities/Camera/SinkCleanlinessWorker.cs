using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Camera.Stores;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores;
using Vivnest.Core.DataStores.Entities;
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
    private readonly IObjectDetector _objectDetector;
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
        IObjectDetector objectDetector,
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
        _objectDetector = objectDetector;
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

        var runtime = _statusStore.GetOrAdd(item.DeviceId);
        var personPresent = false;

        // One detection pass feeds two independent uses (ADR-034's
        // follow-up): a person inside the ROI gates classification below;
        // any other detection inside the ROI outside ExpectedClasses gets
        // flagged regardless of whether a person is also present.
        if (item.ObjectDetection is { Enabled: true } detectionOptions)
        {
            var detections = _objectDetector.Detect(imageBytes, detectionOptions);

            personPresent = detections.Any(d =>
                string.Equals(d.ClassName, "person", StringComparison.OrdinalIgnoreCase)
                && IsWithinRoi(d, detectionOptions));

            if (personPresent)
            {
                runtime.LastPersonSeenUtc = item.CapturedAtUtc;

                _logger.LogInformation(
                    "Person detected at {DeviceId}'s sink area; skipping cleanliness classification for this capture.",
                    item.DeviceId);
            }

            await FlagUnusualObjectsAsync(item, detections, detectionOptions, cancellationToken);
        }

        if (personPresent)
            return;

        var result = _classifier.Classify(imageBytes, item.Options);

        if (result is null)
            return;

        var isDirty = !result.IsClean && result.Confidence >= item.Options.ConfidenceThreshold;
        var isClean = !isDirty;

        // LastSinkClean is null only on this process's first observation
        // for this device since restart - same restart-safety baseline
        // MotionSensorMonitorWorker uses, so a restart never reports a
        // phantom transition. No longer gates whether an event fires (every
        // classification does, by design - see ADR-034's follow-up), but
        // still carried in the payload as Changed so a consumer that only
        // cares about transitions (e.g. Cloud's Telegram alert) can filter
        // for that itself.
        var changed = runtime.LastSinkClean is { } previous && previous != isClean;

        runtime.LastSinkClean = isClean;

        _logger.LogInformation(
            "Sink cleanliness for {DeviceId}: {State}{Changed} (confidence={Confidence:F2})",
            item.DeviceId,
            isDirty ? "dirty" : "clean",
            changed ? ", changed" : "",
            result.Confidence);

        // Every classification is persisted and queued, not just
        // transitions - the dashboard's Events feed should show
        // sink-cleanliness readings the same way CameraCaptured shows
        // every capture, not only state changes.
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
                Changed = changed,
                result.Confidence,
                item.BlobContainer,
                item.BlobName,
                // Timing only, never identity - see ADR-034's follow-up
                // ("no person identification").
                LastPersonSeenUtc = runtime.LastPersonSeenUtc,
            },
        };

        await PersistAndQueueAsync(deviceEvent, item.DeviceId, "SinkCleanliness", cancellationToken);
    }

    private async Task FlagUnusualObjectsAsync(
        SinkCleanlinessWorkItem item,
        IReadOnlyList<Detection> detections,
        ObjectDetectionOptions options,
        CancellationToken cancellationToken)
    {
        var expected = new HashSet<string>(options.ExpectedClasses, StringComparer.OrdinalIgnoreCase);

        var unusual = detections
            .Where(d => IsWithinRoi(d, options))
            .Where(d => !string.Equals(d.ClassName, "person", StringComparison.OrdinalIgnoreCase))
            .Where(d => !expected.Contains(d.ClassName))
            .ToList();

        if (unusual.Count == 0)
            return;

        _logger.LogInformation(
            "Unusual object(s) detected at {DeviceId}: {Classes}",
            item.DeviceId,
            string.Join(", ", unusual.Select(d => d.ClassName)));

        var deviceEvent = new DeviceEvent
        {
            EventId = Guid.NewGuid(),
            AgentId = _agentOptions.AgentId,
            TenantId = _agentOptions.TenantId,
            SiteId = _agentOptions.SiteId,
            DeviceId = item.DeviceId,
            DeviceType = DeviceType.Camera,
            EventType = DeviceEventTypes.UnusualObjectDetected,
            Severity = EventSeverity.Information,
            OccurredAtUtc = item.CapturedAtUtc,
            Data = new
            {
                Objects = unusual.Select(d => new { d.ClassName, d.Confidence }).ToArray(),
                item.BlobContainer,
                item.BlobName,
            },
        };

        await PersistAndQueueAsync(deviceEvent, item.DeviceId, "UnusualObjectDetected", cancellationToken);
    }

    // Detection box counted as "at the sink" if its center falls inside
    // the ROI - simpler and more intuitive than a partial-overlap/IoU
    // rule, and avoids over-triggering on something that just clips the
    // ROI's edge.
    private static bool IsWithinRoi(Detection detection, ObjectDetectionOptions options)
    {
        var centerX = (detection.X1 + detection.X2) / 2;
        var centerY = (detection.Y1 + detection.Y2) / 2;

        return centerX >= options.RoiLeft
            && centerX <= options.RoiRight
            && centerY >= options.RoiTop
            && centerY <= options.RoiBottom;
    }

    private async Task PersistAndQueueAsync(
        DeviceEvent deviceEvent,
        string deviceId,
        string eventTypeForLogging,
        CancellationToken cancellationToken)
    {
        DeviceEventEntity? entity = await _deviceEventWriter.SaveAsync(deviceEvent, cancellationToken);

        if (entity == null)
        {
            _logger.LogWarning(
                "Unable to persist {EventType} DeviceEvent for {DeviceId}",
                eventTypeForLogging,
                deviceId);

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
