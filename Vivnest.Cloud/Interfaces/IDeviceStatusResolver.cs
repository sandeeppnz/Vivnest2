using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Interfaces;

// AgentCascade distinguishes "Offline because the agent process itself is
// down" (every device on it inherits Offline) from "Offline because this
// specific device reported it" - the former is already covered by a single
// AgentOffline notification, so it's a distinct case, not just Status.
// HomeAssistantCascade is the same idea one level down: this device relies
// on HA, and the agent's HA connection - not the agent itself - is what's
// stale, so nothing about the device's own state can currently be trusted.
// StatusSinceUtc is null when there's genuinely no reliable "since when" -
// e.g. the Unknown gap right after an agent recovery, before the device
// itself has reported anything.
public readonly record struct DeviceStatusResult(
    DeviceHeartbeatStatus Status,
    bool AgentCascade,
    bool HomeAssistantCascade,
    DateTime? StatusSinceUtc);

public interface IDeviceStatusResolver
{
    DeviceStatusResult Determine(
        DeviceHeartbeatEntity device,
        AgentHeartbeatEntity? agent);
}
