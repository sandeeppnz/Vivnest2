namespace Vivnest.Core.Devices.MotionSensor.Models;

public sealed record MotionSensorReadingFailureData(
    string AgentId,
    string DeviceId,
    DateTime TimestampUtc,
    string? ErrorCode,
    string? ExceptionMessage);
