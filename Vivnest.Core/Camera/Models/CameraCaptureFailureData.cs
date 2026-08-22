namespace Vivnest.Core.Camera.Models;

public sealed record CameraCaptureFailureData(
    string AgentId,
    string DeviceId,
    DateTime TimestampUtc,
    string? ErrorCode,
    string? ExceptionMessage);