using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Abstraction.Agent.Events;

using Vivnest.Core.Camera.Models;
using Vivnest.Core.Devices.Stores;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Capabilities.Camera;

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

                await _dispatcher.PublishAsync(new CameraCaptureCompletedEvent(result, triggerReason), cancellationToken);

                _logger.LogInformation(
                    "Camera capture reported for {DeviceId}.",
                    cameraOptions.DeviceId);
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
