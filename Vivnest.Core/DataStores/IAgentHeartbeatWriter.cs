using Vivnest.Core.DataStores.Entities;
using Vivnest.Domain.Agents;

namespace Vivnest.Core.DataStores;

// Write-only on purpose - see IDeviceHeartbeatWriter for the reasoning;
// this GetAsync had zero callers too. Cloud reads heartbeats through its
// own IAgentHeartbeatReader.
public interface IAgentHeartbeatWriter
{
    Task<AgentHeartbeatEntity> SaveAsync(
        AgentHeartbeat heartbeat,
        CancellationToken cancellationToken = default);
}
