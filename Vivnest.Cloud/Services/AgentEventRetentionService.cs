using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Interfaces;
using Vivnest.Cloud.Options;

namespace Vivnest.Cloud.Services;

public sealed class AgentEventRetentionService : IAgentEventRetentionService
{
    private readonly IAgentEventReader _agentEvents;
    private readonly AgentEventRetentionOptions _options;
    private readonly ILogger<AgentEventRetentionService> _logger;

    public AgentEventRetentionService(
        IAgentEventReader agentEvents,
        IOptions<AgentEventRetentionOptions> options,
        ILogger<AgentEventRetentionService> logger)
    {
        _agentEvents = agentEvents;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Agent event retention disabled.");
            return;
        }

        var cutoffUtc = DateTime.UtcNow.AddDays(-_options.RetentionDays);

        _logger.LogInformation(
            "Deleting AgentEvent rows older than {CutoffUtc:u} (retention: {RetentionDays} days).",
            cutoffUtc,
            _options.RetentionDays);

        var deleted = await _agentEvents.DeleteOlderThanAsync(cutoffUtc, cancellationToken);

        _logger.LogInformation("Deleted {Count} AgentEvent row(s).", deleted);
    }
}
