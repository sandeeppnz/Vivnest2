namespace Vivnest.Abstraction.Agent.Capabilities;

// RUNTIME capability lifecycle: is this implementation currently running,
// in this Agent process, right now? Owned by ICapability/CapabilityHost
// and reset every time the Agent restarts.
//
// NOT the same concept as Vivnest.Core.Enums.CapabilityStatus
// (Active/Retired), which is the CATALOGUE lifecycle: is this capability
// *definition* usable at all, tenant-wide and independent of any Agent
// (ADR-062). The two are different state machines over different things -
// a Retired capability that is still Running somewhere is a perfectly
// coherent state, and so is an Active one that is Failed on every Agent.
//
// They collide by name often enough that command routing has to alias one
// of them (`using RuntimeCapabilityStatus = ...`). Do not "simplify" them
// into a single enum: there is no ordering between Retired and Failed,
// and no Agent can answer whether a definition is still offered.
public enum CapabilityStatus
{
    Registered,
    Starting,
    Running,
    Stopping,
    Stopped,
    Failed
}