namespace Vivnest.Agent.Runtime.Events;

public sealed record SmartPlugPowerStateChangedEvent(
    string DeviceId,
    bool IsOn,
    DateTime ChangedAtUtc);
