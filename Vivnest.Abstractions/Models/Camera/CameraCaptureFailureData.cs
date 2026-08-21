namespace Vivnest.Abstractions.Models.Camera;

public sealed record CameraCaptureFailureData(
    string AgentId,
    string DeviceId,
    DateTime TimestampUtc,
    string? ErrorCode,
    string? ExceptionMessage);