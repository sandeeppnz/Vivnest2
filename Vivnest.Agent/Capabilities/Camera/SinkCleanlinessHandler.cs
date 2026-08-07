using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Vivnest.Agent.Interfaces;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Utils;

namespace Vivnest.Agent.Capabilities.Camera;

// A second handler on CameraCaptureCompletedEvent (multicast dispatch
// already supports this - same shape as MotionTriggerResolverHandler on
// MotionSensorStateChangedEvent) - opt-in per camera via
// DeviceOptions.SinkCleanliness, a no-op for every camera that doesn't set
// it. See ADR-032.
//
// Deliberately thin: only the cheap, synchronous checks happen here. The
// actual download+classify+persist work runs on SinkCleanlinessWorker,
// off of EventDispatcher's synchronous dispatch chain entirely - see
// ADR-034. This handler's only job is to decide "does this capture need
// analysis?" and, if so, hand it off without blocking the capture pipeline
// on ONNX inference.
public sealed class SinkCleanlinessHandler : IEventHandler<CameraCaptureCompletedEvent>
{
    private readonly ILogger<SinkCleanlinessHandler> _logger;
    private readonly IDeviceRuntimeStore _deviceRegistry;
    private readonly ChannelWriter<SinkCleanlinessWorkItem> _writer;

    public SinkCleanlinessHandler(
        ILogger<SinkCleanlinessHandler> logger,
        IDeviceRuntimeStore deviceRegistry,
        ChannelWriter<SinkCleanlinessWorkItem> writer)
    {
        _logger = logger;
        _deviceRegistry = deviceRegistry;
        _writer = writer;
    }

    public Task HandleAsync(
        CameraCaptureCompletedEvent @event,
        CancellationToken cancellationToken)
    {
        var capture = @event.Result;

        if (capture.BlobName is null || capture.BlobContainer is null)
            return Task.CompletedTask;

        DeviceOptions camera;

        try
        {
            camera = _deviceRegistry.GetDevice(capture.DeviceId, DeviceType.Camera);
        }
        catch (KeyNotFoundException)
        {
            return Task.CompletedTask;
        }

        if (camera.SinkCleanliness is not { Enabled: true } options)
            return Task.CompletedTask;

        var workItem = new SinkCleanlinessWorkItem(
            capture.DeviceId,
            capture.BlobContainer,
            capture.BlobName,
            capture.CapturedAtUtc,
            options,
            camera.ObjectDetection);

        // TryWrite, not WriteAsync - the channel is unbounded so this never
        // actually has to wait, and this handler must stay a true
        // fire-and-forget enqueue, not something that can block the
        // capture pipeline on backpressure.
        if (!_writer.TryWrite(workItem))
        {
            _logger.LogWarning(
                "Failed to enqueue sink-cleanliness work for {DeviceId}",
                capture.DeviceId);
        }

        return Task.CompletedTask;
    }
}
