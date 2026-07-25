using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Services;
using Vivnest.Core.Constants;
using Vivnest.Core.Enums;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Models;
using Vivnest.Core.Models.Camera;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Workers;

public sealed class CaptureWorker : BackgroundService
{
    private readonly ICaptureService _captureService;
    private readonly IAgentGateway _gateway;
    private readonly CaptureStatusStore _statusStore;
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly ILogger<CaptureWorker> _logger;

    public CaptureWorker(
        ICaptureService captureService,
        IAgentGateway gateway,
        CaptureStatusStore statusStore,
        IDeviceRegistry deviceRegistry,
        ILogger<CaptureWorker> logger)
    {
        _captureService = captureService;
        _gateway = gateway;
        _statusStore = statusStore;
        _deviceRegistry = deviceRegistry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Capture Worker started.");

        var cameras = _deviceRegistry.GetCameras();

        if (cameras.Count == 0)
        {
            _logger.LogWarning(
                "No enabled cameras configured. Capture Worker has nothing to do.");
            return;
        }

        // Each camera runs its own independent capture loop (own interval,
        // own failure handling) so one slow/broken camera never blocks or
        // skews the schedule of the others.
        var loops = cameras.Select(camera =>
            RunCaptureLoopAsync(camera, stoppingToken));

        await Task.WhenAll(loops);
    }

    private async Task RunCaptureLoopAsync(
        DeviceOptions cameraOptions,
        CancellationToken stoppingToken)
    {
        var captureStatus = _statusStore.GetOrAdd(cameraOptions.DeviceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _captureService.CaptureAsync(
                    cameraOptions,
                    stoppingToken);

                if (result.Success)
                {
                    captureStatus.LastCaptureUtc = result.CapturedAt;
                    captureStatus.LastBlobName = result.BlobName;
                    captureStatus.LastError = null;

                    var deviceEvent = new DeviceEvent
                    {
                        Id = Guid.NewGuid(),

                        DeviceId = result.DeviceId,

                        DeviceType = DeviceType.Camera,

                        EventType = EventTypes.CameraCaptured,

                        Severity = EventSeverity.Information,

                        Timestamp = result.CapturedAt,

                        Data = new CameraCapturedData
                        {
                            BlobName = result.BlobName!,
                            CapturedAt = result.CapturedAt
                        }
                    };

                    await _gateway.PublishEventAsync(
                        deviceEvent,
                        stoppingToken);

                    _logger.LogInformation(
                        "Camera capture published for device {DeviceId}.",
                        cameraOptions.DeviceId);
                }
                else
                {
                    captureStatus.LastError = result.Error;
                    captureStatus.LastFailureUtc = DateTime.UtcNow;

                    _logger.LogWarning(
                        "Capture failed for device {DeviceId}: {Error}",
                        cameraOptions.DeviceId,
                        result.Error);
                }
            }
            catch (Exception ex)
            {
                captureStatus.LastError = ex.Message;
                captureStatus.LastFailureUtc = DateTime.UtcNow;

                _logger.LogError(
                    ex,
                    "Capture failed for device {DeviceId}.",
                    cameraOptions.DeviceId);
            }

            var delay = cameraOptions.Settings.CaptureInterval;

            _logger.LogInformation(
                "Device {DeviceId} sleeping for {Delay}. Current: Local={NowLocal:yyyy-MM-dd HH:mm:ss}, UTC={NowUtc:yyyy-MM-dd HH:mm:ss}Z. Next capture: Local={NextLocal:yyyy-MM-dd HH:mm:ss}, UTC={NextUtc:yyyy-MM-dd HH:mm:ss}Z",
                cameraOptions.DeviceId,
                delay,
                DateTime.Now,
                DateTime.UtcNow,
                DateTime.Now.Add(delay),
                DateTime.UtcNow.Add(delay));

            await Task.Delay(delay, stoppingToken);
        }
    }
}
