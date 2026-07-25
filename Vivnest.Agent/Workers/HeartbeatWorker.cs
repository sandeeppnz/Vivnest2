using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Services;
using Vivnest.Core.Enums;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Models;
using Vivnest.Core.Models.Camera;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Workers;

public sealed class HeartbeatWorker : BackgroundService
{
    private readonly IAgentGateway _gateway;
    private readonly CaptureStatusStore _statusStore;
    private readonly IDeviceRegistry _deviceRegistry;
    private readonly AgentOptions _agentOptions;
    private readonly HeartbeatOptions _heartbeatOptions;
    private readonly ILogger<HeartbeatWorker> _logger;

    public HeartbeatWorker(
        IAgentGateway gateway,
        CaptureStatusStore statusStore,
        IDeviceRegistry deviceRegistry,
        IOptions<AgentOptions> agentOptions,
        IOptions<HeartbeatOptions> heartbeatOptions,
        ILogger<HeartbeatWorker> logger)
    {
        _gateway = gateway;
        _statusStore = statusStore;
        _deviceRegistry = deviceRegistry;
        _agentOptions = agentOptions.Value;
        _heartbeatOptions = heartbeatOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Heartbeat Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var cameras = _deviceRegistry.GetCameras();

            foreach (var camera in cameras)
            {
                try
                {
                    var captureStatus = _statusStore.GetOrAdd(camera.DeviceId);

                    var heartbeat = new Heartbeat
                    {
                        AgentId = _agentOptions.AgentId,
                        DeviceId = camera.DeviceId,

                        Status = captureStatus.LastError is null
                            ? HeartbeatStatus.Healthy
                            : HeartbeatStatus.Unhealthy,

                        Version = _agentOptions.Version,
                        LastSeenUtc = DateTime.UtcNow,
                        LastCaptureUtc = captureStatus.LastCaptureUtc,

                        BlobName = captureStatus.LastBlobName,

                        Error = captureStatus.LastError
                    };

                    await _gateway.SaveHeartbeatAsync(
                        heartbeat,
                        stoppingToken);

                    _logger.LogDebug(
                        "Heartbeat published for device {DeviceId}.",
                        camera.DeviceId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Heartbeat publish failed for device {DeviceId}.",
                        camera.DeviceId);
                }
            }

            var delay = _heartbeatOptions.HeartbeatInterval;

            _logger.LogInformation(
                "Sleeping for {Delay}. Current: Local={NowLocal:yyyy-MM-dd HH:mm:ss}, UTC={NowUtc:yyyy-MM-dd HH:mm:ss}Z. Next heartbeat: Local={NextLocal:yyyy-MM-dd HH:mm:ss}, UTC={NextUtc:yyyy-MM-dd HH:mm:ss}Z",
                delay,
                DateTime.Now,
                DateTime.UtcNow,
                DateTime.Now.Add(delay),
                DateTime.UtcNow.Add(delay));

            await Task.Delay(delay, stoppingToken);
        }
    }
}
