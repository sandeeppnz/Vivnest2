using Microsoft.Extensions.Logging;
using Vivnest.Core.Interfaces.Stores;
using Vivnest.Core.Models.Heartbeats;

namespace Vivnest.Agent.Runtime.Publishers;

public sealed class AgentHeartbeatPublisher(
    ILogger<AgentHeartbeatPublisher> logger,
    IAgentHeartbeatStore agentHeartbeatStore) : IAgentHeartbeatPublisher
{
    private readonly IAgentHeartbeatStore _agentHeartbeatStore = agentHeartbeatStore;

    private readonly ILogger<AgentHeartbeatPublisher> _logger = logger;

    public Task PublishAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken = default)
    {
        return _agentHeartbeatStore.SaveAsync(
                   heartbeat,
                   cancellationToken);
    }
}
