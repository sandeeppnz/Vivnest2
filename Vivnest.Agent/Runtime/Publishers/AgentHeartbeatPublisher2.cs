using Microsoft.Extensions.Logging;
using Vivnest.Agent.Runtime.Dispatching;
using Vivnest.Agent.Runtime.Events;
using Vivnest.Core.Constants;
using Vivnest.Core.Interfaces;

namespace Vivnest.Agent.Runtime.Publishers;

public class AgentHeartbeatPublisher2 : ICapabilityHandler<AgentHeartbeatReceivedEvent>
{
    private readonly ILogger<AgentHeartbeatPublisher2> _logger;
    private readonly IQueuePublisher _queuePublisher;

    public AgentHeartbeatPublisher2(
        ILogger<AgentHeartbeatPublisher2> logger,
        IQueuePublisher queuePublisher)
    {
        _logger = logger;
        _queuePublisher = queuePublisher;
    }

    public async Task HandleAsync(
        AgentHeartbeatReceivedEvent @event,
        CancellationToken cancellationToken)
    {
        try
        {
            await _queuePublisher.PublishAsync(
                QueueNames.AgentHeartbeat,
                @event.Heartbeat,
                cancellationToken);

            _logger.LogInformation(
                "Published AgentHeartbeat for {AgentId}",
                @event.Heartbeat.AgentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed publishing AgentHeartbeat for {AgentId}",
                @event.Heartbeat.AgentId);

            throw;
        }
    }
}
