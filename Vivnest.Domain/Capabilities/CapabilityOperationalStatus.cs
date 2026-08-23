namespace Vivnest.Domain.Capabilities;

// Decision-log.md ADR-078 - the Phase 8 spec's own "stub, don't overbuild"
// formula: Running iff (Agent Healthy AND capability enabled AND, where a
// runtime signal actually exists for this capability type, that signal
// says active). Unknown when the owning Agent's health can't vouch for
// anything the device last reported. Deliberately a single tri-state, not
// a richer capability-specific health model - "later capabilities can
// emit richer health information" per the spec.
public enum CapabilityOperationalStatus
{
    Running,
    NotRunning,
    Unknown
}
