using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Events;
using Vivnest.Core.Runtime;
using Vivnest.Core.Options;
using Vivnest.Agent.Configuration;

namespace Vivnest.Agent.Shell;

// Deliberately a separate BackgroundService from AgentHeartbeatWorker, not
// folded into its loop - a metrics-sampling failure here must never be
// able to stop the liveness heartbeat from publishing (they're unrelated
// concerns with different consumers: this drives a dashboard chart,
// AgentHeartbeatWorker drives Online/Offline notifications). See
// decision-log.md ADR-018's AgentEvent follow-up.
public sealed class AgentMetricsWorker : BackgroundService
{
    private readonly IEventHandler<AgentMetricsSampledEvent> _handler;
    private readonly INetworkUsageTracker _networkUsageTracker;
    private readonly AgentMetricsOptions _options;
    private readonly ILogger<AgentMetricsWorker> _logger;

    private TimeSpan? _lastTotalProcessorTime;
    private DateTime? _lastCpuSampleUtc;

    public AgentMetricsWorker(
        IEventHandler<AgentMetricsSampledEvent> handler,
        INetworkUsageTracker networkUsageTracker,
        IOptions<AgentMetricsOptions> options,
        ILogger<AgentMetricsWorker> logger)
    {
        _handler = handler;
        _networkUsageTracker = networkUsageTracker;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Agent metrics collection disabled.");
            return;
        }

        using var timer = new PeriodicTimer(_options.Interval);

        do
        {
            try
            {
                var nowUtc = DateTime.UtcNow;

                using var process = Process.GetCurrentProcess();
                var cpuUsagePercent = SampleCpuUsagePercent(process, nowUtc);

                var bytesUploaded = _networkUsageTracker.TakeBytesUploaded();

                await _handler.HandleAsync(
                    new AgentMetricsSampledEvent(nowUtc, cpuUsagePercent, process.WorkingSet64, bytesUploaded),
                    stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sample agent metrics.");
            }

        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    // Null on the first tick of the process's lifetime - there's no prior
    // sample yet to diff against, and a fake 0%/100% would be misleading.
    private double? SampleCpuUsagePercent(Process process, DateTime nowUtc)
    {
        var totalProcessorTime = process.TotalProcessorTime;

        double? cpuUsagePercent = null;

        if (_lastCpuSampleUtc is { } lastSampleUtc && _lastTotalProcessorTime is { } lastProcessorTime)
        {
            var elapsedWallClock = nowUtc - lastSampleUtc;
            var elapsedCpuTime = totalProcessorTime - lastProcessorTime;

            if (elapsedWallClock > TimeSpan.Zero)
            {
                cpuUsagePercent = elapsedCpuTime.TotalMilliseconds
                    / (elapsedWallClock.TotalMilliseconds * Environment.ProcessorCount)
                    * 100;
            }
        }

        _lastCpuSampleUtc = nowUtc;
        _lastTotalProcessorTime = totalProcessorTime;

        return cpuUsagePercent;
    }
}
