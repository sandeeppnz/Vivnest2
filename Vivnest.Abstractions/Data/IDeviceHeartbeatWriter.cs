using Vivnest.Abstractions.Domain;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Abstractions.Data;

public interface IDeviceHeartbeatWriter
{
    Task<DeviceHeartbeatEntity> SaveAsync(
        DeviceHeartbeat heartbeat,
        CancellationToken cancellationToken = default);

    Task<DeviceHeartbeat?> GetAsync(
        string tenantId,
        string siteId,
        string agentId,
        string deviceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceHeartbeat>> GetByAgentAsync(
        string tenantId,
        string siteId,
        string agentId,
        CancellationToken cancellationToken = default);
}
