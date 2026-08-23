using Vivnest.Domain.Devices;

namespace Vivnest.Capabilities.Bridges.HomeAssistant;

public sealed record HomeAssistantStateChangedEvent(
    string EntityId,
    string DeviceId,
    DeviceType DeviceType,
    string EventType,
    string? State,
    string? PreviousState,
    IReadOnlyDictionary<string, string?> Attributes,
    DateTime ChangedAtUtc);
