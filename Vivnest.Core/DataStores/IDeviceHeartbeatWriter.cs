using Vivnest.Core.DataStores.Entities;
using Vivnest.Domain.Devices;

namespace Vivnest.Core.DataStores;

// Write-only on purpose. GetAsync/GetByAgentAsync sat here with zero
// callers, and their entity->model mapping carried a latent identity bug
// the whole time (AgentId mapped from the composite PartitionKey,
// "tenant|site|agent", instead of the AgentId column) - the exact
// dead-code-waiting-to-be-reused shape that already bit twice today.
// Cloud reads heartbeats through its own IDeviceHeartbeatReader.
public interface IDeviceHeartbeatWriter
{
    Task<DeviceHeartbeatEntity> SaveAsync(
        DeviceHeartbeat heartbeat,
        CancellationToken cancellationToken = default);
}
