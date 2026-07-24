using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Services;
using Vivnest.Core.Models;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Workers;

public class CaptureWorker : BackgroundService
{
    private readonly ICaptureService _captureService;
    private readonly CaptureStatus _captureStatus;

    private readonly CameraOptions _options;
    private readonly ILogger<CaptureWorker> _logger;

    public CaptureWorker(
        ICaptureService captureService,
        CaptureStatus captureStatus,

        IOptions<CameraOptions> options,
        ILogger<CaptureWorker> logger)
    {
        _captureService = captureService;
        _captureStatus = captureStatus;

        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Capture Worker started. Interval: {Interval} minutes",
            _options.CaptureIntervalMinutes);


        while (!stoppingToken.IsCancellationRequested)
        {

            try
            {
                _captureStatus.HasStarted = true;
                var result = await _captureService.CaptureAsync(stoppingToken);

                if (result.Success)
                {
                    _captureStatus.LastCaptureUtc = result.CapturedAt;
                    _captureStatus.LastBlobName = result.BlobName;
                    _captureStatus.LastError = null;


                    _logger.LogInformation(
                        "Image stored at {Blob}",
                        result.BlobName);
                }
                else
                {
                    _captureStatus.LastError = result.Error;

                    _logger.LogWarning(
                        "Capture failed: {Error}",
                        result.Error);
                }
            }
            catch (Exception ex)
            {
                _captureStatus.LastError = ex.Message;
                _logger.LogError(ex, "Capture failed.");
            }

            var delay = TimeSpan.FromMinutes(_options.CaptureIntervalMinutes);

            var nextCaptureUtc = DateTime.UtcNow.Add(delay);
            var nextCaptureLocal = DateTime.Now.Add(delay);

            _logger.LogInformation(
                "Next capture scheduled: Local={LocalTime:yyyy-MM-dd HH:mm:ss} | UTC={UtcTime:yyyy-MM-dd HH:mm:ss}Z",
                nextCaptureLocal,
                nextCaptureUtc);

            await Task.Delay(delay, stoppingToken);
        }
    }
}