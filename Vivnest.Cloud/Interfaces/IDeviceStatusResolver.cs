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
// ParentDeviceCascade is the same idea again, but for a device reached
// through another registered device (e.g. a motion sensor through a Tapo
// hub) rather than through the agent's own HA connection - see
// DeviceOptions.ParentDeviceId.
// StatusSinceUtc is null when there's genuinely no reliable "since when" -
// e.g. the Unknown gap right after an agent recovery, before the device
// itself has reported anything.
public readonly record struct DeviceStatusResult(
    DeviceHeartbeatStatus Status,
    bool AgentCascade,
    bool HomeAssistantCascade,
    bool ParentDeviceCascade,
    DateTime? StatusSinceUtc);

public interface IDeviceStatusResolver
{
    // parentDevice is the DeviceHeartbeatEntity for device.ParentDeviceId,
    // already fetched by the caller (this resolver doesn't own storage
    // access) - pass null if device has no ParentDeviceId, or if the parent
    // hasn't reported a heartbeat at all yet. Only one level is resolved -
    // a parent's own ParentDeviceId (if it somehow had one) is not
    // followed, since nothing in the product today nests hubs within hubs.
    DeviceStatusResult Determine(
        DeviceHeartbeatEntity device,
        AgentHeartbeatEntity? agent,
        DeviceHeartbeatEntity? parentDevice = null);
}
