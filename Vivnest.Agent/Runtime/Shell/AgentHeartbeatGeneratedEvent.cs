using Vivnest.Core.Domain;

namespace Vivnest.Agent.Runtime.Shell;

public sealed record AgentHeartbeatGeneratedEvent(
    AgentHeartbeat Heartbeat);
