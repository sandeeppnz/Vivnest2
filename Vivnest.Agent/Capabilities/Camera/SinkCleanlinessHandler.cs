using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Interfaces;
using Vivnest.Core.Camera.Stores;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Queues;
using Vivnest.Core.Queues.Models;
using Vivnest.Core.Utils;

namespace Vivnest.Agent.Capabilities.Camera;

// A second handler on CameraCaptureCompletedEvent (multicast dispatch
// already supports this - same shape as MotionTriggerResolverHandler on
// MotionSensorStateChangedEvent) - opt-in per camera via
// DeviceOptions.SinkCleanliness, a no-op for every camera that doesn't set
// it. See ADR-032.
//
// Deliberately thin: only the cheap, synchronous checks happen here. The
// actual download+classify+persist work runs on a separate Ai-role agent
// entirely (ADR-035, ADR-034's design 3) - this handler's only job is to
// decide "does this capture need analysis?" and, if so, publish a
// classify request to Cloud without blocking the capture pipeline on a
// network round-trip.
public sealed class SinkCleanlinessHandler : IEventHandler<CameraCaptureCompletedEvent>
{
    private readonly ILogger<SinkCleanlinessHandler> _logger;
    private readonly IDeviceRuntimeStore _deviceRegistry;
    private readonly ICaptureStatusStore _statusStore;
    private readonly AgentOptions _agentOptions;
    private readonly MessagingOptions _messagingOptions;
    private readonly IQueuePublisher _queuePublisher;

    public SinkCleanlinessHandler(
        ILogger<SinkCleanlinessHandler> logger,
        IDeviceRuntimeStore deviceRegistry,
        ICaptureStatusStore statusStore,
        IOptions<AgentOptions> agentOptions,
        IOptions<MessagingOptions> messagingOptions,
        IQueuePublisher queuePublisher)
    {
        _logger = logger;
        _deviceRegistry = deviceRegistry;
        _statusStore = statusStore;
        _agentOptions = agentOptions.Value;
        _messagingOptions = messagingOptions.Value;
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

        if (string.IsNullOrWhiteSpace(_agentOptions.AiAgentId))
        {
            _logger.LogWarning(
                "SinkCleanliness is enabled for {DeviceId} but Agent:AiAgentId is not configured; nowhere to route classification.",
                capture.DeviceId);

            return;
        }

        // A burst fires captures every BurstInterval (e.g. 30s) instead of
        // the normal Schedule.Interval (e.g. 15min) - analyzing all ~20 of
        // them is both wasteful and was the actual root cause of a real
        // starvation bug (ADR-034's follow-up). Only the burst's first and
        // last captures get analyzed: first because it's the capture most
        // likely to actually catch someone at the sink (taken right when
        // motion fired), which is what the person-gate below needs to set
        // LastPersonSeenUtc promptly; last because it's the fairest "is
        // this clean now" read, once the activity's likely concluded.
        // "Last" is predicted, not exact - this runs before
        // CameraCaptureWorker's own next-tick check, not after.
        var runtime = _statusStore.GetOrAdd(capture.DeviceId);

        if (runtime.BurstUntilUtc is { } burstUntilUtc && DateTime.UtcNow < burstUntilUtc)
        {
            var isFirstBurstCapture = runtime.LastAnalyzedBurstUntilUtc != burstUntilUtc;

            var nextTickUtc = DateTime.UtcNow + (runtime.BurstInterval ?? TimeSpan.Zero);
            var isLastBurstCapture = nextTickUtc >= burstUntilUtc;

            if (!isFirstBurstCapture && !isLastBurstCapture)
                return;

            runtime.LastAnalyzedBurstUntilUtc = burstUntilUtc;
        }

        var message = new ClassifyCaptureQueueMessage(
            AgentId: _agentOptions.AiAgentId,
            OriginAgentId: _agentOptions.AgentId,
            OriginTenantId: _agentOptions.TenantId,
            OriginSiteId: _agentOptions.SiteId,
            DeviceId: capture.DeviceId,
            BlobContainer: capture.BlobContainer,
            BlobName: capture.BlobName,
            CapturedAtUtc: capture.CapturedAtUtc,
            SinkCleanlinessRoi: options,
            ObjectDetectionRoi: camera.ObjectDetection,
            IssuedAtUtc: DateTime.UtcNow);

        // Unlike the old channel's TryWrite, a queue publish is a real
        // network call and can throw - caught here, not propagated, since
        // this handler must stay fire-and-forget and never let an
        // AI-pipeline hiccup surface as a capture failure (same reasoning
        // ADR-033's follow-up already established for this class).
        try
        {
            await _queuePublisher.PublishAsync(
                _messagingOptions.ClassifyRequestQueue,
                message,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to publish classify request for {DeviceId}",
                capture.DeviceId);
        }
    }
}
