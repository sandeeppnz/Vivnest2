namespace Vivnest.Cloud.Api.Dtos;

public sealed record DeviceSummaryDto(
    string DeviceId,
    string Name,
    string DeviceType,
    string Status,
    DateTime? StatusSinceUtc,
    DateTime LastHeartbeatUtc,
    DateTime? LastActivityUtc,
    TimeSpan HeartbeatInterval,
    string AgentId,
    string TenantId,
    string SiteId,
    string? Error,
    string? ParentDeviceId,
    string Timezone,
    string Location,
    string Brand,
    string Model,
    string Firmware) : IMonitorable
{
    string IMonitorable.Id => DeviceId;
}
