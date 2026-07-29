using System;
using System.Collections.Generic;
using System.Text;
using Vivnest.Core.Models.Heartbeats;

namespace Vivnest.Agent.Runtime.Events;

public sealed record AgentHeartbeatReceivedEvent(
    AgentHeartbeat Heartbeat);
