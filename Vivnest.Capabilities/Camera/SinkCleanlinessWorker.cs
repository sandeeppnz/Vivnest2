using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Devices.Stores;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Options;
using Vivnest.Core.Queues;
using Vivnest.Core.Queues.Models;
using Vivnest.Core.Storage;
using AzureQueueMessage = Azure.Storage.Queues.Models.QueueMessage;
using Vivnest.Domain.Capabilities;
using Vivnest.Domain.Devices;
using Vivnest.Domain.Shared;

namespace Vivnest.Capabilities.Camera;

// Runs on a High-type agent only (ADR-035, ADR-034's design 3) - polls
// MessagingOptions.ClassifyCommandQueue directly, same
// poll/delete-first/filter-by-AgentId shape as PlatformCommandPollingWorker, just
// on a shorter interval since this carries automatic, routine,
// latency-sensitive traffic rather than a rare manual admin action. A
// separate BackgroundService, same shape as PlatformAgentMetricsWorker (own loop,
// own try/catch so a hiccup here can't touch anything else), so ONNX
// inference and a blob download never delay anything on this process's
// other workers.
public sealed class SinkCleanlinessWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly QueueServiceClient _queueServiceClient;
    private readonly IDeviceRuntimeStateStore _statusStore;
    private readonly ISinkCleanlinessClassifier _classifier;
    private readonly IObjectDetector _objectDetector;
    private readonly AzureBlobStorageClient _blobStorage;
    private readonly AgentOptions _agentOptions;
    private readonly MessagingOptions _messagingOptions;
    private readonly AiClassificationOptions _aiClassificationOptions;
    private readonly IDeviceEventWriter _deviceEventWriter;
    private readonly IQueuePublisher _queuePublisher;
    private readonly ILogger<SinkCleanlinessWorker> _logger;

    public SinkCleanlinessWorker(
        QueueServiceClient queueServiceClient,
        IDeviceRuntimeStateStore statusStore,
        ISinkCleanlinessClassifier classifier,
        IObjectDetector objectDetector,
        AzureBlobStorageClient blobStorage,
        IOptions<AgentOptions> agentOptions,
        IOptions<MessagingOptions> messagingOptions,
        IOptions<AiClassificationOptions> aiClassificationOptions,
        IDeviceEventWriter deviceEventWriter,
        IQueuePublisher queuePublisher,
        ILogger<SinkCleanlinessWorker> logger)
    {
        _queueServiceClient = queueServiceClient;
        _statusStore = statusStore;
        _classifier = classifier;
        _objectDetector = objectDetector;
        _blobStorage = blobStorage;
        _agentOptions = agentOptions.Value;
        _messagingOptions = messagingOptions.Value;
        _aiClassificationOptions = aiClassificationOptions.Value;
        _deviceEventWriter = deviceEventWriter;
        _queuePublisher = queuePublisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_messagingOptions.ClassifyCommandQueue))
        {
            _logger.LogWarning(
                "Messaging:ClassifyCommandQueue not configured; SinkCleanlinessWorker has nothing to poll.");

            return;
        }

        var queue = _queueServiceClient.GetQueueClient(_messagingOptions.ClassifyCommandQueue);

        await queue.CreateIfNotExistsAsync(cancellationToken: stoppingToken);

        _logger.LogInformation(
            "SinkCleanlinessWorker started, polling {Queue} every {Interval}.",
            _messagingOptions.ClassifyCommandQueue,
            PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await queue.ReceiveMessagesAsync(
                    maxMessages: 10,
                    cancellationToken: stoppingToken);

                foreach (var message in response.Value)
                {
                    await HandleMessageAsync(queue, message, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SinkCleanlinessWorker poll tick failed.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task HandleMessageAsync(
        QueueClient queue,
        AzureQueueMessage message,
        CancellationToken cancellationToken)
    {
        // Delete first, not after processing - same "rare transient
        // failure loses the request" tradeoff PlatformCommandPollingWorker
        // already accepts. No worse than before this class polled a
        // queue: its per-item try/catch below already never retried a
        // failed ProcessAsync even when this ran off an in-process
        // channel (ADR-034/035) - this relocates that "no retry"
        // contract, it doesn't weaken it.
        await queue.DeleteMessageAsync(
            message.MessageId,
            message.PopReceipt,
            cancellationToken);

        ClassifyCaptureQueueMessage? item;

        try
        {
            item = JsonSerializer.Deserialize<ClassifyCaptureQueueMessage>(message.MessageText);
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Unable to deserialize classify command message {MessageId}; discarding.",
                message.MessageId);

            return;
        }

        if (item is null)
        {
            _logger.LogWarning(
                "Classify command message {MessageId} deserialized to null; discarding.",
                message.MessageId);

            return;
        }

        if (!string.Equals(item.AgentId, _agentOptions.AgentId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Classify command addressed to {TargetAgentId}, not this agent ({AgentId}); discarding.",
                item.AgentId,
                _agentOptions.AgentId);

            return;
        }

        try
        {
            await ProcessAsync(item, cancellationToken);
        }
        catch (Exception ex)
        {
            // Deliberately not rethrown - same reasoning as ADR-033's
            // follow-up fix: a sink-cleanliness hiccup must never surface
            // as anything else failing. This loop has to survive it too,
            // or every later capture's classification would silently
            // stop along with it.
            _logger.LogError(
                ex,
                "Sink cleanliness analysis failed for {DeviceId}",
                item.DeviceId);
        }
    }

    // Dispatches on which single capability this message carries (ADR-036)
    // - SinkCleanliness and ObjectDetection now route independently, each
    // possibly to a different High-type agent, so a message only ever has one job
    // to do. Previously this ran both in one pass and used ObjectDetection's
    // person-in-frame result to gate whether SinkCleanliness ran at all;
    // that coupling relied on both running synchronously in the same call
    // and can't survive two independently-queued messages - dropped
    // entirely rather than rebuilt (confirmed acceptable trade-off,
    // decision-log.md ADR-036). runtime.LastPersonSeenUtc is still recorded
    // when ObjectDetection runs, purely informational.
    private async Task ProcessAsync(ClassifyCaptureQueueMessage item, CancellationToken cancellationToken)
    {
        var imageBytes = await _blobStorage.DownloadAsync(
            item.BlobContainer,
            item.BlobName,
            cancellationToken);

        var deviceModelConfig = _aiClassificationOptions.Devices
            .FirstOrDefault(d => string.Equals(d.DeviceId, item.DeviceId, StringComparison.Ordinal));

        switch (item.Capability)
        {
            case ClassifyCapability.ObjectDetection:
                await ProcessObjectDetectionAsync(item, imageBytes, deviceModelConfig, cancellationToken);
                break;
            case ClassifyCapability.SinkCleanliness:
                await ProcessSinkCleanlinessAsync(item, imageBytes, deviceModelConfig, cancellationToken);
                break;
            default:
                _logger.LogWarning(
                    "Classify command for {DeviceId} carries unknown capability {Capability}; discarding.",
                    item.DeviceId,
                    item.Capability);
                break;
        }
    }

    private async Task ProcessObjectDetectionAsync(
        ClassifyCaptureQueueMessage item,
        byte[] imageBytes,
        AiDeviceClassification? deviceModelConfig,
        CancellationToken cancellationToken)
    {
        if (item.ObjectDetectionRoi is not { Enabled: true } detectionRoi)
            return;

        if (deviceModelConfig?.ObjectDetection is not { } detectionModel)
        {
            _logger.LogWarning(
                "ObjectDetection is enabled for {DeviceId} but this High-type agent has no matching AiClassification config; skipping.",
                item.DeviceId);

            return;
        }

        var detectionOptions = new ObjectDetectionOptions
        {
            RoiLeft = detectionRoi.RoiLeft,
            RoiTop = detectionRoi.RoiTop,
            RoiRight = detectionRoi.RoiRight,
            RoiBottom = detectionRoi.RoiBottom,
            ModelPath = detectionModel.ModelPath,
            ConfidenceThreshold = detectionModel.ConfidenceThreshold,
            ExpectedClasses = detectionModel.ExpectedClasses,
        };

        var detections = _objectDetector.Detect(imageBytes, detectionOptions);

        var personPresent = detections.Any(d =>
            string.Equals(d.ClassName, "person", StringComparison.OrdinalIgnoreCase)
            && IsWithinRoi(d, detectionOptions));

        if (personPresent)
        {
            var runtime = _statusStore.GetOrAdd(item.DeviceId);
            runtime.LastPersonSeenUtc = item.CapturedAtUtc;
        }

        await PersistObjectDetectionEventAsync(item, detections, detectionOptions, personPresent, cancellationToken);
    }

    private async Task ProcessSinkCleanlinessAsync(
        ClassifyCaptureQueueMessage item,
        byte[] imageBytes,
        AiDeviceClassification? deviceModelConfig,
        CancellationToken cancellationToken)
    {
        if (item.SinkCleanlinessRoi is not { Enabled: true } sinkRoi)
            return;

        var runtime = _statusStore.GetOrAdd(item.DeviceId);

        if (deviceModelConfig?.SinkCleanliness is not { } sinkModel)
        {
            _logger.LogWarning(
                "SinkCleanliness is enabled for {DeviceId} but this High-type agent has no matching AiClassification config; skipping.",
                item.DeviceId);

            return;
        }

        var sinkOptions = new SinkCleanlinessOptions
        {
            RoiLeft = sinkRoi.RoiLeft,
            RoiTop = sinkRoi.RoiTop,
            RoiRight = sinkRoi.RoiRight,
            RoiBottom = sinkRoi.RoiBottom,
            ModelPath = sinkModel.ModelPath,
            ConfidenceThreshold = sinkModel.ConfidenceThreshold,
        };

        var result = _classifier.Classify(imageBytes, sinkOptions);

        if (result is null)
            return;

        var isDirty = !result.IsClean && result.Confidence >= sinkOptions.ConfidenceThreshold;
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
            // The capturing agent's identity, not this process's own -
            // this High-type worker classifies captures from a different
            // agent's device (ADR-035), so the event must be attributed
            // to whoever actually owns the device.
            AgentId = item.OriginAgentId,
            TenantId = item.OriginTenantId,
            SiteId = item.OriginSiteId,
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

    // Fires on every capture ObjectDetection runs on, not just ones with
    // something unusual - same "every classification, not just the
    // interesting case" cadence SinkCleanliness already uses. Carries every
    // ROI-contained detection with its box, so the dashboard can draw them
    // over the photo rather than only ever seeing a class-name summary.
    private async Task PersistObjectDetectionEventAsync(
        ClassifyCaptureQueueMessage item,
        IReadOnlyList<Detection> detections,
        ObjectDetectionOptions options,
        bool personPresent,
        CancellationToken cancellationToken)
    {
        var expected = new HashSet<string>(options.ExpectedClasses, StringComparer.OrdinalIgnoreCase);
        var withinRoi = detections.Where(d => IsWithinRoi(d, options)).ToList();

        var objects = withinRoi
            .Select(d =>
            {
                var isPerson = string.Equals(d.ClassName, "person", StringComparison.OrdinalIgnoreCase);
                var unusual = !isPerson && !expected.Contains(d.ClassName);

                return new
                {
                    d.ClassName,
                    d.Confidence,
                    d.X1,
                    d.Y1,
                    d.X2,
                    d.Y2,
                    Unusual = unusual,
                };
            })
            .ToArray();

        var hasUnusual = objects.Any(o => o.Unusual);

        if (hasUnusual)
        {
            _logger.LogInformation(
                "Unusual object(s) detected at {DeviceId}: {Classes}",
                item.DeviceId,
                string.Join(", ", objects.Where(o => o.Unusual).Select(o => o.ClassName)));
        }

        var deviceEvent = new DeviceEvent
        {
            EventId = Guid.NewGuid(),
            // See the identical comment in ProcessAsync's DeviceEvent -
            // this must be the capturing agent's identity, not this
            // process's own.
            AgentId = item.OriginAgentId,
            TenantId = item.OriginTenantId,
            SiteId = item.OriginSiteId,
            DeviceId = item.DeviceId,
            DeviceType = DeviceType.Camera,
            EventType = DeviceEventTypes.ObjectsDetected,
            // Warning, not Information, when something outside
            // ExpectedClasses showed up - lets the dashboard's existing
            // severity-based badge coloring (EventsFeed.tsx) distinguish
            // "counter looks normal" from "something new is there" without
            // any extra dashboard-side logic.
            Severity = hasUnusual ? EventSeverity.Warning : EventSeverity.Information,
            OccurredAtUtc = item.CapturedAtUtc,
            Data = new
            {
                Objects = objects,
                PersonPresent = personPresent,
                HasUnusualObjects = hasUnusual,
                item.BlobContainer,
                item.BlobName,
            },
        };

        await PersistAndQueueAsync(deviceEvent, item.DeviceId, "ObjectsDetected", cancellationToken);
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
