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
    private readonly CaptureStatus _captureStatus;
    private readonly AgentOptions _agentOptions;
    private readonly DeviceOptions _cameraOptions;
    private readonly HeartbeatOptions _heartbeatOptions;
    private readonly ILogger<HeartbeatWorker> _logger;

    public HeartbeatWorker(
        IAgentGateway gateway,
        CaptureStatus captureStatus,
        IOptions<AgentOptions> agentOptions,
        IOptions<DeviceOptions> cameraOptions,
        IOptions<HeartbeatOptions> heartbeatOptions,
        ILogger<HeartbeatWorker> logger)
    {
        _gateway = gateway;
        _captureStatus = captureStatus;
        _agentOptions = agentOptions.Value;
        _cameraOptions = cameraOptions.Value;
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
            try
            {
                var heartbeat = new Heartbeat
                {
                    AgentId = _agentOptions.AgentId,
                    DeviceId = _cameraOptions.DeviceId,

                    Status = _captureStatus.LastError is null
                        ? HeartbeatStatus.Healthy
                        : HeartbeatStatus.Unhealthy,

                    Version = _agentOptions.Version,
                    LastSeenUtc = DateTime.UtcNow,
                    LastCaptureUtc = _captureStatus.LastCaptureUtc,

                    BlobName = _captureStatus.LastBlobName,

                    Error = _captureStatus.LastError
                };

                await _gateway.PublishHeartbeatAsync(
                    heartbeat,
                    stoppingToken);

                _logger.LogDebug(
                    "Heartbeat published.");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Heartbeat publish failed.");
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