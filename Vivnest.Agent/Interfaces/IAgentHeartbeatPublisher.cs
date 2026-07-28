using Vivnest.Core.Models.Heartbeats;

public interface IAgentHeartbeatPublisher
{
    Task PublishAsync(
        AgentHeartbeat heartbeat,
        CancellationToken cancellationToken = default);
}