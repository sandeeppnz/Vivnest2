using Vivnest.Core.Interfaces;
using Vivnest.Core.Models.Heartbeats;

namespace Vivnest.Core.Domain;

public record AgentHeartbeatPublished(
    AgentHeartbeat Heartbeat) : IMessage
{
    public Task PublishAsync<T>(T message)
    {
        throw new NotImplementedException();
    }
}