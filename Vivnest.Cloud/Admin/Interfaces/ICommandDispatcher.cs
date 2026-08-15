using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin.Interfaces;

// Decision-log.md ADR-079 - Admin -> Command -> Agent, the single entry
// point every command type (RestartAgent/RefreshConfiguration/
// ApplyConfiguration/ExecuteCapability) goes through: validate ownership,
// persist, enqueue.
public interface ICommandDispatcher
{
    // Returns null only when the target Agent itself can't be identified
    // (doesn't exist, or isn't owned by this tenant) - matches
    // AgentsFunction.RestartAgent's existing "404, no row created"
    // convention, since there's no valid TargetAgentId to attach a
    // command to. Every other validation failure (wrong Device, capability
    // not assigned, assigned to a different ExecutingAgentId, an Agent
    // already busy with a disruptive command) still creates a real,
    // persisted command row - straight into Failed, never enqueued -
    // rather than silently refusing, so "who tried to do what, and why
    // did it not happen" stays answerable from command history alone
    // (spec's own Audit requirement, section 28).
    Task<AgentCommandDto?> DispatchAsync(
        TenantContext tenant,
        string commandType,
        string targetAgentId,
        string requestedBy,
        string? targetDeviceId = null,
        string? capabilityId = null,
        string? payload = null,
        CancellationToken cancellationToken = default);
}
