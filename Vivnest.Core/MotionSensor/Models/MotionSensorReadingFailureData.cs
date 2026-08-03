namespace Vivnest.Core.MotionSensor.Models;

public sealed record MotionSensorReadingFailureData(
    string AgentId,
    string DeviceId,
    DateTime TimestampUtc,
    string? ErrorCode,
    string? ExceptionMessage);
