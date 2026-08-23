using Vivnest.Domain.Agents;

namespace Vivnest.Agent.Runtime.Shell;

public sealed record AgentHeartbeatGeneratedEvent(
    AgentHeartbeat Heartbeat);
