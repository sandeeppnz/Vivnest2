namespace Vivnest.Domain.Devices;

// Which writer last reported this device's heartbeat - lets Cloud tell
// apart a device that depends on the agent's Home Assistant connection
// from one that doesn't, since only the former should cascade to Unknown
// when that connection specifically (not the whole agent) goes stale.
public enum DeviceHeartbeatSource
{
    Native,
    HomeAssistant
}
