using Vivnest.Core.Enums;

namespace Vivnest.Core.Models;

public abstract class BaseIdentity
{
    public required string TenantId { get; init; }

    public required string SiteId { get; init; }

    public required string AgentId { get; init; }
}
public sealed class DeviceEvent: BaseIdentity
{
    public Guid Id { get; init; }
    //public string AgentFirmwareVersion { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public DeviceType DeviceType { get; init; }
    public string EventType { get; init; } = string.Empty;
    public DateTime OccurredAtUtc { get; init; }
    public EventSeverity Severity { get; init; }
    public object? Data { get; init; }
}
