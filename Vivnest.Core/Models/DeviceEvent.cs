using Vivnest.Core.Enums;

namespace Vivnest.Core.Models;

public sealed class DeviceEvent
{
    public Guid Id { get; init; }
    public string AgentId { get; init; } = string.Empty;
    public string AgentVersion { get; init; } = string.Empty;

    public string DeviceId { get; init; } = string.Empty;
    public DeviceType DeviceType { get; init; }
    public string EventType { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public EventSeverity Severity { get; init; }
    public object? Data { get; init; }
}