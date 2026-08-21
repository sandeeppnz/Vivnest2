using Vivnest.Core.Enums;

namespace Vivnest.Abstractions.Models.Triggers;

// Deliberately generic - "who got triggered and what kind of device it
// is," not "run a capture." Any number of action-specific handlers can
// subscribe (CaptureOnTriggerHandler today, a future
// TurnOnPlugOnTriggerHandler, etc.), each deciding independently whether
// DeviceType applies to it - see decision-log.md's motion-triggered-capture
// ADR for why this stays one shared event rather than a per-action-type one.
public sealed record DeviceTriggeredEvent(
    string DeviceId,
    DeviceType DeviceType,
    string Reason,
    DateTime TriggeredAtUtc);
