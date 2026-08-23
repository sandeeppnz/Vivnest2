using Vivnest.Domain.Devices;

namespace Vivnest.Core.Events;

public sealed record DeviceHeartbeatGeneratedEvent(
    DeviceHeartbeat Heartbeat);
