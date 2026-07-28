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
    private readonly AgentOptions _agentOptions;
    private readonly ILogger<CaptureWorker> _logger;

    public CaptureWorker(
        ICaptureService captureService,
        IAgentGateway gateway,
        CaptureStatusStore statusStore,
        IDeviceRegistry deviceRegistry,
        IOptions<AgentOptions> agentOptions,
        ILogger<CaptureWorker> logger)
    {
        _captureService = captureService;
        _gateway = gateway;
        _statusStore = statusStore;
        _deviceRegistry = deviceRegistry;
        _agentOptions = agentOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("Capture Worker started.");

        var cameras = _deviceRegistry.GetCameras();

        if (cameras.Count == 0)
        {
            _logger.LogWarning(
                "No enabled cameras configured. Capture Worker has nothing to do.");

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
        var captureStatus =
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
                    captureStatus.LastCaptureUtc = result.CapturedAtUtc;
                    captureStatus.LastBlobName = result.BlobName;
                    captureStatus.LastError = null;

                    await _gateway.PublishCaptureAsync(
                        result,
                        _agentOptions,
                        stoppingToken);

                    _logger.LogInformation(
                        "Camera capture reported for {DeviceId}.",
                        cameraOptions.DeviceId);
                }
                else
                {
                    captureStatus.LastError = result.Error;
                    captureStatus.LastFailureUtc = DateTime.UtcNow;

                    await _gateway.UpdateDeviceFailureAsync(
                        _agentOptions,
                        cameraOptions.DeviceId,
                        result.Error ?? "Capture failed",
                        stoppingToken);

                    _logger.LogWarning(
                        "Capture failed for {DeviceId}: {Error}",
                        cameraOptions.DeviceId,
                        result.Error);
                }
            }
            catch (Exception ex)
            {
                captureStatus.LastError = ex.Message;
                captureStatus.LastFailureUtc = DateTime.UtcNow;

                await _gateway.UpdateDeviceFailureAsync(
                    _agentOptions,
                    cameraOptions.DeviceId,
                    ex.Message,
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