namespace Vivnest.Abstractions.Models.Api;

public sealed record CapturePageDto(
    IReadOnlyList<DeviceEventDto> Captures,
    bool HasMore);
