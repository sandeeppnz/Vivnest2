namespace Vivnest.Agent.Runtime.Events;

public sealed record CameraCaptureFailedEvent(
    string DeviceId,
    DateTime TimestampUtc,
    string? Error
);
