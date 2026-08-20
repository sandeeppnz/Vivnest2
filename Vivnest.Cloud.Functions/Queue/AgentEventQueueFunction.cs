using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Cloud.Functions.Queue;

// Sprint 8 - mirrors DeviceEventQueueFunction exactly, including the
// rethrow so Azure Functions retries.
public class AgentEventQueueFunction
{
    private readonly ILogger<AgentEventQueueFunction> _logger;
    private readonly IAgentEventQueueHandler _handler;

    public AgentEventQueueFunction(
        ILogger<AgentEventQueueFunction> logger,
        IAgentEventQueueHandler handler)
    {
        _logger = logger;
        _handler = handler;
    }

    [Function(nameof(AgentEventQueueFunction))]
    public async Task Run(
        [QueueTrigger("agent-events")]
            AgentEventQueueMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            await _handler.HandleAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed processing agent event {PartitionKey}/{RowKey}",
                message.PartitionKey,
                message.RowKey);

            throw;
        }
    }
}
