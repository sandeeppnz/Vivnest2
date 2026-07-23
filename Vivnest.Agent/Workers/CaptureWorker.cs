using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Options;
using Vivnest.Agent.Services;

namespace Vivnest.Agent.Workers;

public class CaptureWorker : BackgroundService
{
    private readonly ICaptureService _captureService;
    private readonly AgentOptions _options;
    private readonly ILogger<CaptureWorker> _logger;

    public CaptureWorker(
        ICaptureService captureService,
        IOptions<AgentOptions> options,
        ILogger<CaptureWorker> logger)
    {
        _captureService = captureService;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Capture Worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var result =
                await _captureService.CaptureAsync(stoppingToken);

            if (result.Success)
            {
                _logger.LogInformation(
                    "Image stored at {Blob}",
                    result.BlobName);
            }

            await Task.Delay(
                TimeSpan.FromMinutes(_options.CaptureIntervalMinutes),
                stoppingToken);
        }
    }
}