using Vivnest.Core.Domain;

namespace Vivnest.Agent.Runtime.Events;

public sealed record AgentHeartbeatRecordedEvent(
    AgentHeartbeat Heartbeat);
