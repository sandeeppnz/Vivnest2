using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Vivnest.Cloud.Interfaces;

namespace Vivnest.Cloud.Functions.Timer;

public class AgentEventRetentionTimerFunction
{
    private readonly ILogger<AgentEventRetentionTimerFunction> _logger;
    private readonly IAgentEventRetentionService _retentionService;

    public AgentEventRetentionTimerFunction(
        ILogger<AgentEventRetentionTimerFunction> logger,
        IAgentEventRetentionService retentionService)
    {
        _logger = logger;
        _retentionService = retentionService;
    }

    // Flat setting name, no double underscore - see DeviceEventRetentionTimerFunction's
    // note (roadmap.md's HealthMonitorCronSchedule gotcha): the %...%
    // placeholder resolver looks up the literal string, which doesn't
    // survive the __ -> : conversion .NET's env-var config provider applies.
    [Function(nameof(AgentEventRetentionTimerFunction))]
    public async Task Run(
        [TimerTrigger("%AgentEventRetentionCronSchedule%")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "AgentEventRetentionTimerFunction running at {Time}.",
            DateTime.UtcNow);

        await _retentionService.RunAsync(cancellationToken);
    }
}
