using Vivnest.Domain.Shared;

namespace Vivnest.Domain.Agents;

public sealed class AgentEvent : BaseIdentity
{
    public Guid EventId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public DateTime OccurredAtUtc { get; init; }
    public EventSeverity Severity { get; init; }
    public object? Data { get; init; }
}
