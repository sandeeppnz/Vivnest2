namespace Vivnest.Core.Devices.SmartPlug.Models;

public sealed class SmartPlugReadingResult
{
    public bool Success { get; init; }
    public string DeviceId { get; set; } = string.Empty;
    public DateTime ReadAtUtc { get; init; }
    public SmartPlugState? State { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
}
