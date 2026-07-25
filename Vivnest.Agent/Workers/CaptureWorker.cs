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
    private readonly CaptureStatus _captureStatus;
    private readonly DeviceOptions _cameraOptions;
    private readonly ILogger<CaptureWorker> _logger;

    public CaptureWorker(
        ICaptureService captureService,
        IAgentGateway gateway,
        CaptureStatus captureStatus,
        IDeviceRegistry deviceRegistry,
        ILogger<CaptureWorker> logger)
    {
        _captureService = captureService;
        _gateway = gateway;
        _captureStatus = captureStatus;
        _cameraOptions = deviceRegistry.GetCamera();
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Capture Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _captureService.CaptureAsync(
                    stoppingToken);

                if (result.Success)
                {
                    _captureStatus.LastCaptureUtc = result.CapturedAt;
                    _captureStatus.LastBlobName = result.BlobName;
                    _captureStatus.LastError = null;

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
                        "Camera capture published.");
                }
                else
                {
                    _captureStatus.LastError = result.Error;
                    _captureStatus.LastFailureUtc = DateTime.UtcNow;

                    _logger.LogWarning(
                        "Capture failed: {Error}",
                        result.Error);
                }
            }
            catch (Exception ex)
            {
                _captureStatus.LastError = ex.Message;
                _captureStatus.LastFailureUtc = DateTime.UtcNow;

                _logger.LogError(
                    ex,
                    "Capture failed.");
            }

            var delay = _cameraOptions.Settings.CaptureInterval;

            _logger.LogInformation(
                "Sleeping for {Delay}. Current: Local={NowLocal:yyyy-MM-dd HH:mm:ss}, UTC={NowUtc:yyyy-MM-dd HH:mm:ss}Z. Next capture: Local={NextLocal:yyyy-MM-dd HH:mm:ss}, UTC={NextUtc:yyyy-MM-dd HH:mm:ss}Z",
                delay,
                DateTime.Now,
                DateTime.UtcNow,
                DateTime.Now.Add(delay),
                DateTime.UtcNow.Add(delay));

            await Task.Delay(delay, stoppingToken);
        }
    }
}