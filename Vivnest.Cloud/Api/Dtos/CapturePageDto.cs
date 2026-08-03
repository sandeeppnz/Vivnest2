namespace Vivnest.Cloud.Api.Dtos;

public sealed record CapturePageDto(
    IReadOnlyList<DeviceEventDto> Captures,
    bool HasMore);
