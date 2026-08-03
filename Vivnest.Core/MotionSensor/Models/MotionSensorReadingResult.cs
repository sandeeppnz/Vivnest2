namespace Vivnest.Core.MotionSensor.Models;

public sealed class MotionSensorReadingResult
{
    public bool Success { get; init; }
    public string DeviceId { get; set; } = string.Empty;
    public DateTime ReadAtUtc { get; init; }
    public MotionSensorState? State { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
}
