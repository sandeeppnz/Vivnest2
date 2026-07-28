using Vivnest.Core.Models.Heartbeats;

namespace Vivnest.Core.Interfaces.Stores;

public interface IDeviceHeartbeatStore
{
    Task SaveAsync(
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