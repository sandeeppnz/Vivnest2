using Vivnest.Abstractions.Domain;
using Vivnest.Core.Enums;

namespace Vivnest.Abstractions.Models.Agent;

public sealed class AgentEvent : BaseIdentity
{
    public Guid EventId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public DateTime OccurredAtUtc { get; init; }
    public EventSeverity Severity { get; init; }
    public object? Data { get; init; }
}
