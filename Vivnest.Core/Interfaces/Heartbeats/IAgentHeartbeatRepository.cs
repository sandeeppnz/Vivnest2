using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vivnest.Core.Models.Heartbeats;

namespace Vivnest.Core.Interfaces.Heartbeats;

public interface IAgentHeartbeatRepository
{
    Task UpsertAsync(
        AgentHeartbeat heartbeat,
        CancellationToken cancellationToken = default);

    Task<AgentHeartbeat?> GetAsync(
        string agentId,
        CancellationToken cancellationToken = default);
}
