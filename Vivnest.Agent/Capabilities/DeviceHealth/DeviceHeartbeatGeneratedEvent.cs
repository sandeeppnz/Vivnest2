using Vivnest.Core.Domain;

namespace Vivnest.Agent.Capabilities.DeviceHealth;

public sealed record DeviceHeartbeatGeneratedEvent(
    DeviceHeartbeat Heartbeat);
