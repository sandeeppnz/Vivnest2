namespace Vivnest.Cloud.Api.Dtos;

public sealed record DeviceSummaryDto(
    string DeviceId,
    string DeviceType,
    string Status,
    DateTime? StatusSinceUtc,
    DateTime LastHeartbeatUtc,
    DateTime? LastActivityUtc,
    TimeSpan HeartbeatInterval,
    string AgentId,
    string TenantId,
    string SiteId,
    string? Error);
