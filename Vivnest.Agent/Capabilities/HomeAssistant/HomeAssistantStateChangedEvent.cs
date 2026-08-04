using Vivnest.Core.Enums;

namespace Vivnest.Agent.Capabilities.HomeAssistant;

public sealed record HomeAssistantStateChangedEvent(
    string EntityId,
    string DeviceId,
    DeviceType DeviceType,
    string EventType,
    string? State,
    string? PreviousState,
    IReadOnlyDictionary<string, string?> Attributes,
    DateTime ChangedAtUtc);
