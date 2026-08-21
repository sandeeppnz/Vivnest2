using Vivnest.Abstractions.Domain;

namespace Vivnest.Agent.Capabilities.DeviceHealth;

public sealed record DeviceHeartbeatGeneratedEvent(
    DeviceHeartbeat Heartbeat);
