using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin.Interfaces;

// One line per step taken, in order, so the dashboard can render the
// whole sequence the way the seed report does. Kind is "device",
// "agent" or "refresh"; Outcome is "published", "unchanged", "blocked",
// "skipped", "queued" or "failed"; Detail carries the publisher's or
// dispatcher's own reason when there is one.
public sealed record PublishAllItem(
    string Kind,
    string Name,
    string Outcome,
    string? Detail);

public sealed record PublishAllReport(
    IReadOnlyList<PublishAllItem> Items,
    bool RefreshQueued);

public interface IAgentPublishAllService
{
    // "Make this agent match the admin screens" as one action (ADR-121):
    // publish every Active device owned by the agent, publish the agent's
    // own configuration, then queue one RefreshConfiguration command -
    // the exact choreography that, done by hand step-by-step, left the
    // 2026-08-29 rebuild stuck on "Enabled assignments: 0" with no error
    // when one step was missed. Unchanged publishes are no-ops (ADR-069
    // hash guard) and the refresh is always queued, so the whole action
    // is idempotent and safe to mash.
    //
    // runtimeAgentId is the /agents/{agentId} identity space; returns
    // null when no registered Agent maps to it (mirrors every other
    // /agents route's 404).
    Task<PublishAllReport?> PublishAllAsync(
        TenantContext tenant,
        string runtimeAgentId,
        string requestedBy,
        CancellationToken cancellationToken = default);
}
