namespace Vivnest.Core.SmartPlug.Models;

public sealed record SmartPlugReadingFailureData(
    string AgentId,
    string DeviceId,
    DateTime TimestampUtc,
    string? ErrorCode,
    string? ExceptionMessage);
