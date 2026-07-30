using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Interfaces;
using Vivnest.Agent.Runtime.Events;
using Vivnest.Core.Camera;
using Vivnest.Core.Options;
using Vivnest.Core.Utils;

namespace Vivnest.Agent.Runtime.Workers;

public sealed class CameraCaptureWorker : BackgroundService
{
    private readonly ICameraCaptureService _captureService;
    private readonly ICapabilityDispatcher _dispatcher;
    private readonly ICaptureStatusStore _statusStore;
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly DeviceHeartbeatOptions _deviceHeartbeatOptions;
    private readonly AgentOptions _agentOptions;
    private readonly ILogger<CameraCaptureWorker> _logger;

    public CameraCaptureWorker(
        ICameraCaptureService captureService,
        ICapabilityDispatcher dispatcher,
        ICaptureStatusStore statusStore,
        IDeviceRegistry deviceRegistry,
        IOptions<AgentOptions> agentOptions,
        IOptions<DeviceHeartbeatOptions> deviceHeartbeatOptions,
        ILogger<CameraCaptureWorker> logger)
    {
        _captureService = captureService;
        _dispatcher = dispatcher;
        _statusStore = statusStore;
        _deviceRegistry = deviceRegistry;
        _deviceHeartbeatOptions = deviceHeartbeatOptions.Value;
        _agentOptions = agentOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("Camera Capture Worker started.");

        var cameras = _deviceRegistry.GetCameras();

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
            try
            {
                var result = await _captureService.CaptureAsync(
                    cameraOptions,
                    stoppingToken);

                if (result.Success)
                {
                    runtime.LastCaptureUtc = result.CapturedAtUtc;
                    runtime.LastBlobName = result.BlobName;

                    // Capture succeeded, so clear any previous capture error.
                    runtime.LastError = null;


                    //Can be sent the capture result
                    await _dispatcher.PublishAsync(new CameraCaptureRecordedEvent(result), stoppingToken);


                    _logger.LogInformation(
                        "Camera capture reported for {DeviceId}.",
                        cameraOptions.DeviceId);
                }
                else
                {
                    // Store runtime state only.
                    runtime.LastFailureUtc = DateTime.UtcNow;
                    runtime.LastError = result.Error;

                    await _dispatcher.PublishAsync(
                        new CameraCaptureFailedEvent(
                            cameraOptions.DeviceId,
                            DateTime.UtcNow,
                            result.Error),
                        stoppingToken);

                    _logger.LogWarning(
                        "Capture failed for {DeviceId}: {Error}",
                        cameraOptions.DeviceId,
                        result.Error);
                }
            }
            catch (Exception ex)
            {
                runtime.LastFailureUtc = DateTime.UtcNow;
                runtime.LastError = ex.Message;

                await _dispatcher.PublishAsync(
                    new CameraCaptureFailedEvent(
                        cameraOptions.DeviceId,
                        DateTime.UtcNow,
                        ex.Message),
                    stoppingToken);


                _logger.LogError(
                    ex,
                    "Capture failed for {DeviceId}.",
                    cameraOptions.DeviceId);
            }

            var delay = cameraOptions.ActivityInterval;

            _logger.LogInformation(
                "Device {DeviceId} sleeping for {Delay}. Current UTC={Now:u}. Next capture UTC={Next:u}",
                cameraOptions.DeviceId,
                delay,
                DateTime.UtcNow,
                DateTime.UtcNow.Add(delay));

            await Task.Delay(delay, stoppingToken);
        }
    }
}