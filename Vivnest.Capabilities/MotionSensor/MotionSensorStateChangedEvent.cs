namespace Vivnest.Capabilities.MotionSensor;

public sealed record MotionSensorStateChangedEvent(
    string DeviceId,
    bool Detected,
    DateTime ChangedAtUtc);
