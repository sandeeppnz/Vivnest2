namespace Vivnest.Cloud.Api.Dtos;

public sealed record DeviceSummaryDto(
    string DeviceId,
    string DeviceType,
    string Status,
    DateTime LastHeartbeatUtc,
    DateTime? LastActivityUtc,
    string? Error);
