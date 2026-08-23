using Vivnest.Domain.Agents;

namespace Vivnest.Agent.Shell;

public sealed record AgentHeartbeatGeneratedEvent(
    AgentHeartbeat Heartbeat);
