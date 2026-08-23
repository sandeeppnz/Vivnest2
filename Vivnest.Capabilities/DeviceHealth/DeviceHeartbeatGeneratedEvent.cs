using Vivnest.Domain.Devices;

namespace Vivnest.Capabilities.DeviceHealth;

public sealed record DeviceHeartbeatGeneratedEvent(
    DeviceHeartbeat Heartbeat);
