using Vivnest.Core.Enums;

namespace Vivnest.Abstractions.Domain;

public sealed class DeviceEvent : BaseIdentity
{
    public Guid EventId { get; init; }
    public string DeviceId { get; init; } = string.Empty;
    public DeviceType DeviceType { get; init; }
    public string EventType { get; init; } = string.Empty;
    public DateTime OccurredAtUtc { get; init; }
    public EventSeverity Severity { get; init; }
    public object? Data { get; init; }
}
