using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Vivnest.Cloud.Interfaces;

namespace Vivnest.Cloud.Functions;

public class HealthMonitorTimerFunction
{
    private readonly ILogger<HealthMonitorTimerFunction> _logger;
    private readonly IHealthMonitorService _healthMonitorService;

    public HealthMonitorTimerFunction(
        ILogger<HealthMonitorTimerFunction> logger,
        IHealthMonitorService healthMonitorService)
    {
        _logger = logger;
        _healthMonitorService = healthMonitorService;
    }

    [Function(nameof(HealthMonitorTimerFunction))]
    public async Task Run(
        [TimerTrigger("%HealthMonitor__CronSchedule%")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "HealthMonitorTimerFunction running at {Time}.",
            DateTime.UtcNow);

        await _healthMonitorService.RunAsync(cancellationToken);
    }
}
