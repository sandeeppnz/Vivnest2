namespace Vivnest.Agent.Runtime.Events;

public sealed record MotionSensorStateChangedEvent(
    string DeviceId,
    bool Detected,
    DateTime ChangedAtUtc);
