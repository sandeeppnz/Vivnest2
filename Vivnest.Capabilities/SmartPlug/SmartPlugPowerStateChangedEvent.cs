namespace Vivnest.Capabilities.SmartPlug;

public sealed record SmartPlugPowerStateChangedEvent(
    string DeviceId,
    bool IsOn,
    DateTime ChangedAtUtc);
