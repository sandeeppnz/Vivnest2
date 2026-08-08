using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Interfaces;
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
// network round-trip. No burst throttling here anymore (removed,
// ADR-035's follow-up) - every capture gets published, burst or not; the
// Ai-agent's own queue poll serializes the work without competing for
// this process's own responsiveness, which is what made throttling
// necessary in the first place back when classification ran in-process.
public sealed class SinkCleanlinessHandler : IEventHandler<CameraCaptureCompletedEvent>
{
    private readonly ILogger<SinkCleanlinessHandler> _logger;
    private readonly IDeviceRuntimeStore _deviceRegistry;
    private readonly AgentOptions _agentOptions;
    private readonly MessagingOptions _messagingOptions;
    private readonly IQueuePublisher _queuePublisher;

    public SinkCleanlinessHandler(
        ILogger<SinkCleanlinessHandler> logger,
        IDeviceRuntimeStore deviceRegistry,
        IOptions<AgentOptions> agentOptions,
        IOptions<MessagingOptions> messagingOptions,
        IQueuePublisher queuePublisher)
    {
        _logger = logger;
        _deviceRegistry = deviceRegistry;
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
