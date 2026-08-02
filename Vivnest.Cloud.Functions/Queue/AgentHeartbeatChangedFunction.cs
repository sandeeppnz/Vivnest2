using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Cloud.Functions.Queue;

public class AgentHeartbeatChangedFunction
{
    private readonly ILogger<AgentHeartbeatChangedFunction> _logger;
    private readonly IHealthMonitorService _healthMonitorService;

    public AgentHeartbeatChangedFunction(
        ILogger<AgentHeartbeatChangedFunction> logger,
        IHealthMonitorService healthMonitorService)
    {
        _logger = logger;
        _healthMonitorService = healthMonitorService;
    }

    [Function(nameof(AgentHeartbeatChangedFunction))]
    public async Task Run(
        [QueueTrigger("agent-heartbeats")]
            AgentHeartbeatQueueMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            await _healthMonitorService.ProcessAgentAsync(
                message.PartitionKey,
                message.RowKey,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed processing agent heartbeat change {PartitionKey}/{RowKey}",
                message.PartitionKey,
                message.RowKey);

            throw; // Important: rethrow so Azure Functions retries
        }
    }
}
