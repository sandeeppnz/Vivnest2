using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vivnest.Agent.Interfaces;
using Vivnest.Core.Devices.Stores;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;
using Vivnest.Core.Utils;

namespace Vivnest.Agent.Capabilities.Camera;

public sealed class CameraCaptureWorker : BackgroundService
{
    private readonly ICameraCaptureService _captureService;
    private readonly ICameraCaptureExecutor _executor;
    private readonly IDeviceRuntimeStateStore _statusStore;
    private readonly IDeviceRuntimeStore _deviceRegistry;
    private readonly ILogger<CameraCaptureWorker> _logger;

    public CameraCaptureWorker(
        ICameraCaptureService captureService,
        ICameraCaptureExecutor executor,
        IDeviceRuntimeStateStore statusStore,
        IDeviceRuntimeStore deviceRegistry,
        ILogger<CameraCaptureWorker> logger)
    {
        _captureService = captureService;
        _executor = executor;
        _statusStore = statusStore;
        _deviceRegistry = deviceRegistry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("Camera Capture Worker started.");

        var cameras = _deviceRegistry.GetDevices()
            .Where(d => d.Type == DeviceType.Camera)
            .ToList();

        if (cameras.Count == 0)
        {
            _logger.LogWarning(
                "No enabled cameras configured. Camera Capture Worker has nothing to do.");

            return;
        }

        var tasks = cameras.Select(camera =>
            RunCaptureLoopAsync(camera, stoppingToken));

        await Task.WhenAll(tasks);
    }

    private async Task RunCaptureLoopAsync(
        DeviceOptions cameraOptions,
        CancellationToken stoppingToken)
    {
        var runtime =
            _statusStore.GetOrAdd(cameraOptions.DeviceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            // CaptureOnTriggerHandler sets BurstUntilUtc/BurstInterval on a
            // trigger; while active, both the "is a capture due" threshold
            // and the sleep between ticks shrink to that cadence, then
            // self-expire back to the normal one once BurstUntilUtc passes
            // - no separate "revert" step needed.
            var inBurst = runtime.BurstUntilUtc is { } burstUntilUtc
                && DateTime.UtcNow < burstUntilUtc;

            var effectiveSnapshotInterval = inBurst
                ? runtime.BurstInterval!.Value
                : cameraOptions.Schedule.Interval;

            var dueForCapture =
                effectiveSnapshotInterval <= TimeSpan.Zero ||
                runtime.LastCaptureUtc is not { } lastCaptureUtc ||
                DateTime.UtcNow - lastCaptureUtc >= effectiveSnapshotInterval;

            if (dueForCapture)
            {
                await _executor.CaptureAsync(
                    cameraOptions,
                    runtime,
                    stoppingToken,
                    inBurst ? runtime.BurstReason : null);
            }
            else
            {
                await ProbeAsync(cameraOptions, runtime, stoppingToken);
            }

            var delay = inBurst
                ? runtime.BurstInterval!.Value
                : cameraOptions.LivenessInterval;

            _logger.LogInformation(
                "Device {DeviceId} sleeping for {Delay}. Current UTC={Now:u}. Next check UTC={Next:u}",
                cameraOptions.DeviceId,
                delay,
                DateTime.UtcNow,
                DateTime.UtcNow.Add(delay));

            await WaitAsync(runtime, delay, stoppingToken);
        }
    }

    // Races the normal delay against CaptureOnTriggerHandler's wake signal,
    // so a burst starts on the spot instead of waiting for whatever's left
    // of a possibly much longer LivenessInterval sleep to elapse.
    private static async Task WaitAsync(
        DeviceRuntimeState runtime,
        TimeSpan delay,
        CancellationToken stoppingToken)
    {
        var delayTask = Task.Delay(delay, stoppingToken);
        var wakeTask = runtime.WakeSignal.WaitAsync(stoppingToken);

        await Task.WhenAny(delayTask, wakeTask);
    }

    private async Task ProbeAsync(
        DeviceOptions cameraOptions,
        DeviceRuntimeState runtime,
        CancellationToken stoppingToken)
    {
        try
        {
            var reachable = await _captureService.CheckReachabilityAsync(
                cameraOptions,
                stoppingToken);

            if (reachable)
            {
                runtime.LastActivityUtc = DateTime.UtcNow;

                _logger.LogDebug(
                    "Liveness probe succeeded for {DeviceId}.",
                    cameraOptions.DeviceId);
            }
            else
            {
                _logger.LogWarning(
                    "Liveness probe failed for {DeviceId}: camera unreachable.",
                    cameraOptions.DeviceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Liveness probe errored for {DeviceId}.",
                cameraOptions.DeviceId);
        }
    }
}
