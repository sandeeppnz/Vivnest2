using Vivnest.Core.Domain;

namespace Vivnest.Capabilities.DeviceHealth;

public sealed record DeviceHeartbeatGeneratedEvent(
    DeviceHeartbeat Heartbeat);
