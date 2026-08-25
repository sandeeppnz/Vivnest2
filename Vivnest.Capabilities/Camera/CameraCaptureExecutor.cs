using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Events;

using Vivnest.Core.Devices.Camera.Models;
using Vivnest.Runtime.State;
using Vivnest.Core.Options;

namespace Vivnest.Capabilities.Camera;

// Extracted from CameraCaptureWorker's own timer-loop body once a second
// real caller (CaptureOnTriggerHandler, motion-triggered capture) needed
// the exact same capture-then-publish logic - see decision-log.md's
// motion-triggered-capture ADR. Deliberately unchanged behavior, just
// relocated so both callers share one implementation instead of two
// drifting copies.
public sealed class CameraCaptureExecutor : ICameraCaptureExecutor
{
    private readonly ICameraCaptureService _captureService;
    private readonly IEventDispatcher _dispatcher;
    private readonly AgentOptions _agentOptions;
    private readonly ILogger<CameraCaptureExecutor> _logger;

    public CameraCaptureExecutor(
        ICameraCaptureService captureService,
        IEventDispatcher dispatcher,
        IOptions<AgentOptions> agentOptions,
        ILogger<CameraCaptureExecutor> logger)
    {
        _captureService = captureService;
        _dispatcher = dispatcher;
        _agentOptions = agentOptions.Value;
        _logger = logger;
    }

    public async Task CaptureAsync(
        DeviceOptions cameraOptions,
        DeviceRuntimeState runtime,
        CancellationToken cancellationToken,
        string? triggerReason = null)
    {
        try
        {
            var result = await _captureService.CaptureAsync(
                cameraOptions,
                cancellationToken);

            if (result.Success)
            {
                runtime.LastCaptureUtc = result.CapturedAtUtc;
                runtime.LastActivityUtc = result.CapturedAtUtc;
                runtime.LastBlobName = result.BlobName;

                // Capture succeeded, so clear any previous capture error.
                runtime.LastError = null;

                await PublishCaptureCompletedSafeAsync(
                    result,
                    triggerReason,
                    cameraOptions.DeviceId,
                    cancellationToken);
            }
            else
            {
                // Store runtime state only.
                runtime.LastFailureUtc = DateTime.UtcNow;
                runtime.LastError = result.Error;

                await PublishCaptureFailedSafeAsync(
                    new CameraCaptureFailureData(
                        _agentOptions.AgentId,
                        cameraOptions.DeviceId,
                        DateTime.UtcNow,
                        result.ErrorCode,
                        result.Error),
                    cancellationToken);

                _logger.LogWarning(
                    "Capture failed for {DeviceId}: {Error}",
                    cameraOptions.DeviceId,
                    result.Error);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller (e.g. graceful shutdown/restart) cancelled us - not a capture
            // failure, so don't record LastError or report a failure event for it.
            throw;
        }
        catch (Exception ex)
        {
            runtime.LastFailureUtc = DateTime.UtcNow;
            runtime.LastError = ex.Message;

            await PublishCaptureFailedSafeAsync(
                new CameraCaptureFailureData(
                    _agentOptions.AgentId,
                    cameraOptions.DeviceId,
                    DateTime.UtcNow,
                    ex.Message,
                    ex.InnerException?.Message),
                cancellationToken);

            _logger.LogError(
                ex,
                "Capture failed for {DeviceId}.",
                cameraOptions.DeviceId);
        }
    }

    // The publish is isolated from the capture it reports, because the two
    // are different failures with different meanings.
    //
    // Before this, the publish sat inside CaptureAsync's own catch: a
    // handler that threw - CameraCaptureHandler rethrows when the
    // DeviceEvent cannot be persisted - was recorded as a CAPTURE failure.
    // It set runtime.LastError, raised a Critical CameraCaptureFailed
    // event, and made the device look broken, after the photo had already
    // been taken and uploaded successfully. A storage problem was reported
    // as a camera problem, which is both wrong and misleading in exactly
    // the place someone would go looking.
    //
    // Swallowed rather than propagated, on the same terms
    // PublishCaptureFailedSafeAsync already accepts: an event that cannot
    // be reported is worth an error in the log, not a false failure on the
    // device and not a dead capture loop.
    private async Task PublishCaptureCompletedSafeAsync(
        CameraCaptureResult result,
        string? triggerReason,
        string deviceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dispatcher.PublishAsync(
                new CameraCaptureCompletedEvent(result, triggerReason),
                cancellationToken);

            _logger.LogInformation(
                "Camera capture reported for {DeviceId}.",
                deviceId);
        }
        catch (Exception ex)
        {
            // The capture itself succeeded and runtime state already says
            // so - deliberately not reverted here.
            _logger.LogError(
                ex,
                "Capture succeeded for {DeviceId} but reporting it failed.",
                deviceId);
        }
    }

    private async Task PublishCaptureFailedSafeAsync(
        CameraCaptureFailureData failure,
        CancellationToken cancellationToken)
    {
        try
        {
            await _dispatcher.PublishAsync(
                new CameraCaptureFailedEvent(failure),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to report capture failure for {DeviceId}.",
                failure.DeviceId);
        }
    }
}
