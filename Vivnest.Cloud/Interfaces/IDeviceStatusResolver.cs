using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Interfaces;

// AgentCascade distinguishes "Offline because the agent process itself is
// down" (every device on it inherits Offline) from "Offline because this
// specific device reported it" - the former is already covered by a single
// AgentOffline notification, so it's a distinct case, not just Status.
public readonly record struct DeviceStatusResult(
    DeviceHeartbeatStatus Status,
    bool AgentCascade);

public interface IDeviceStatusResolver
{
    DeviceStatusResult Determine(
        DeviceHeartbeatEntity device,
        AgentHeartbeatEntity? agent);
}
