using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vivnest.Core.Models.Heartbeats;

namespace Vivnest.Core.Interfaces.Stores;

public interface IAgentHeartbeatStore
{
    Task SaveAsync(
        AgentHeartbeat heartbeat,
        CancellationToken cancellationToken = default);

    Task<AgentHeartbeat?> GetAsync(
        string agentId,
        CancellationToken cancellationToken = default);
}
