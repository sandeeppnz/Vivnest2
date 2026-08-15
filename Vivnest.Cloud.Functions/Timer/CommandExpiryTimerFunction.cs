using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Vivnest.Cloud.Interfaces;

namespace Vivnest.Cloud.Functions.Timer;

public class CommandExpiryTimerFunction
{
    private readonly ILogger<CommandExpiryTimerFunction> _logger;
    private readonly ICommandExpiryService _expiryService;

    public CommandExpiryTimerFunction(
        ILogger<CommandExpiryTimerFunction> logger,
        ICommandExpiryService expiryService)
    {
        _logger = logger;
        _expiryService = expiryService;
    }

    // Flat setting name, no double underscore - same
    // DeviceEventRetentionTimerFunction/AgentEventRetentionTimerFunction
    // gotcha (roadmap.md's HealthMonitorCronSchedule note): the %...%
    // placeholder resolver looks up the literal string, which doesn't
    // survive the __ -> : conversion .NET's env-var config provider
    // applies.
    [Function(nameof(CommandExpiryTimerFunction))]
    public async Task Run(
        [TimerTrigger("%CommandExpiryCronSchedule%")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "CommandExpiryTimerFunction running at {Time}.",
            DateTime.UtcNow);

        await _expiryService.RunAsync(cancellationToken);
    }
}
