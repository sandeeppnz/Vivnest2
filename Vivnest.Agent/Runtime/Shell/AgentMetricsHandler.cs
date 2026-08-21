using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Runtime.Dispatching;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Runtime.Shell;

// No queue publish - a metrics sample needs no Cloud-side reaction (no
// notification), only storage for the dashboard's chart to read later.
public class AgentMetricsHandler : IEventHandler<AgentMetricsSampledEvent>
{
    private readonly ILogger<AgentMetricsHandler> _logger;
    private readonly AgentOptions _agentOptions;
    private readonly IAgentEventWriter _agentEventWriter;

    public AgentMetricsHandler(
        ILogger<AgentMetricsHandler> logger,
        IOptions<AgentOptions> agentOptions,
        IAgentEventWriter agentEventWriter)
    {
        _logger = logger;
        _agentOptions = agentOptions.Value;
        _agentEventWriter = agentEventWriter;
    }

    public async Task HandleAsync(
        AgentMetricsSampledEvent @event,
        CancellationToken cancellationToken)
    {
        try
        {
            var agentEvent = new AgentEvent
            {
                EventId = Guid.NewGuid(),
                AgentId = _agentOptions.AgentId,
                TenantId = _agentOptions.TenantId,
                SiteId = _agentOptions.SiteId,
                EventType = AgentEventTypes.MetricsReported,
                Severity = EventSeverity.Information,
                OccurredAtUtc = @event.SampledAtUtc,
                Data = new
                {
                    @event.CpuUsagePercent,
                    @event.MemoryUsedBytes,
                    @event.BytesUploaded
                }
            };

            var entity = await _agentEventWriter.SaveAsync(
                agentEvent,
                cancellationToken);

            if (entity == null)
            {
                _logger.LogWarning(
                    "Unable to persist AgentEvent for {AgentId}",
                    _agentOptions.AgentId);

                return;
            }

            _logger.LogDebug(
                "Metrics sample persisted for {AgentId}: CPU={Cpu}%, Memory={Memory} bytes",
                _agentOptions.AgentId,
                @event.CpuUsagePercent,
                @event.MemoryUsedBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist metrics sample for {AgentId}",
                _agentOptions.AgentId);
        }
    }
}
