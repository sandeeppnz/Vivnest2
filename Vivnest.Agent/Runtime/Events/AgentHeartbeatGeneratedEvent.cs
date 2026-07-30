using Vivnest.Core.Domain;

namespace Vivnest.Agent.Runtime.Events;

public sealed record AgentHeartbeatGeneratedEvent(
    AgentHeartbeat Heartbeat);
