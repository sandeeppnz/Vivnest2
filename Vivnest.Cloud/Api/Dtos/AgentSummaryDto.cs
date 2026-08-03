namespace Vivnest.Cloud.Api.Dtos;

public sealed record AgentSummaryDto(
    string AgentId,
    string HostName,
    string Status,
    DateTime StartedUtc,
    DateTime LastHeartbeatUtc,
    TimeSpan HeartbeatInterval,
    DateTime StatusSinceUtc,
    string TenantId,
    string SiteId,
    string? Error);
