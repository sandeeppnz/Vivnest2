using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Agent.Interfaces;
using Vivnest.Core.DataStores;
using Vivnest.Core.Options;
using Vivnest.Core.Queues;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Agent.Runtime.Shell;

public class AgentHeartbeatHandler : IEventHandler<AgentHeartbeatGeneratedEvent>
{
    private readonly ILogger<AgentHeartbeatHandler> _logger;
    private readonly MessagingOptions _messagingOptions;
    private readonly IQueuePublisher _queuePublisher;
    private readonly IAgentHeartbeatWriter _agentHeartbeatWriter;


    public AgentHeartbeatHandler(
        ILogger<AgentHeartbeatHandler> logger,
        IOptions<MessagingOptions> messagingOptions,
        IAgentHeartbeatWriter agentHeartbeatWriter,
        IQueuePublisher queuePublisher)
    {
        _logger = logger;
        _agentHeartbeatWriter = agentHeartbeatWriter;
        _messagingOptions = messagingOptions.Value;
        _queuePublisher = queuePublisher;
    }

    public async Task HandleAsync(
        AgentHeartbeatGeneratedEvent @event,
        CancellationToken cancellationToken)
    {
        try
        {
            var entity = await _agentHeartbeatWriter.SaveAsync(
                  @event.Heartbeat,
                  cancellationToken);

            _logger.LogInformation(
                "Agent heartbeat persisted for {AgentId}",
                @event.Heartbeat.AgentId);

            await _queuePublisher.PublishAsync(
                _messagingOptions.AgentHeartbeatQueue,
                new AgentHeartbeatQueueMessage
                {
                    PartitionKey = entity.PartitionKey,
                    RowKey = entity.RowKey
                },
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
