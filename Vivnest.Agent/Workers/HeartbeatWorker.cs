using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Heartbeat;
using Vivnest.Core.Models;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Workers;

public sealed class HeartbeatWorker : BackgroundService
{
    private readonly IHeartbeatService _heartbeatService;
    private readonly HeartbeatOptions _heartbeatOptions;
    private readonly AgentOptions _agentOptions;
    private readonly CameraOptions _cameraOptions;
    private readonly CaptureStatus _captureStatus;
    private readonly ILogger<HeartbeatWorker> _logger;

    public HeartbeatWorker(
        IHeartbeatService heartbeatService,
        IOptions<AgentOptions> agentOptions,
        IOptions<HeartbeatOptions> heartbeatOptions,
        IOptions<CameraOptions> cameraOptions,
        CaptureStatus captureStatus,
        ILogger<HeartbeatWorker> logger)
    {
        _heartbeatService = heartbeatService;
        _heartbeatOptions = heartbeatOptions.Value;
        _cameraOptions = cameraOptions.Value;
        _agentOptions = agentOptions.Value;
        _captureStatus = captureStatus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Heartbeat Worker started. Heartbeat interval: {HeartbeatInterval}",
            _heartbeatOptions.HeartbeatInterval);


        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                HeartbeatStatus status;
                string? error = _captureStatus.LastError;

                if (!_captureStatus.HasStarted)
                {
                    // Capture worker hasn't attempted its first capture yet.
                    status = HeartbeatStatus.Healthy;
                }
                else if (!_captureStatus.LastCaptureUtc.HasValue)
                {
                    // Capture has been attempted but has never succeeded.
                    status = HeartbeatStatus.Unhealthy;
                }
                else
                {
                    var captureExpiry =
                        _captureStatus.LastCaptureUtc.Value +
                        _cameraOptions.CaptureInterval +
                        _heartbeatOptions.CaptureGraceInterval;

                    var captureHealthy = DateTime.UtcNow <= captureExpiry;

                    status = captureHealthy
                        ? HeartbeatStatus.Healthy
                        : HeartbeatStatus.Unhealthy;
                }

                var heartbeat = new Heartbeat
                {
                    AgentId = _agentOptions.AgentId,
                    Version = _agentOptions.Version,
                    Status = status,
                    LastSeenUtc = DateTime.UtcNow,
                    LastCaptureUtc = _captureStatus.LastCaptureUtc,
                    BlobName = _captureStatus.LastBlobName,
                    Error = error
                };

                await _heartbeatService.SendAsync(
                    heartbeat,
                    stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send heartbeat.");
            }

            await Task.Delay(
                _heartbeatOptions.HeartbeatInterval,
                stoppingToken);
        }
    }
}