namespace Vivnest.Agent.Capabilities.SmartPlug;

public sealed record SmartPlugPowerStateChangedEvent(
    string DeviceId,
    bool IsOn,
    DateTime ChangedAtUtc);
