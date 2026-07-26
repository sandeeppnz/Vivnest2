using Vivnest.Core.Models.Heartbeats;

namespace Vivnest.Core.Interfaces.Heartbeats;

public interface IDeviceHeartbeatRepository
{
    Task UpsertAsync(
        DeviceHeartbeat heartbeat,
        CancellationToken cancellationToken = default);

    Task<DeviceHeartbeat?> GetAsync(
        string agentId,
        string deviceId,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<DeviceHeartbeat>> GetByAgentAsync(
        string agentId,
        CancellationToken cancellationToken = default);
}
