namespace Vivnest.Core.Enums;

// The terminal/non-terminal split of AgentCommandStatus was written out
// three independent times before this existed:
//
//   - AgentCommandManagementService.IsTerminal (Cloud) - enum-based, gates
//     the duplicate/late-callback no-op.
//   - CommandExpiryService.ExpirableStatuses (Cloud) - the exact
//     complement, spelled out as its own HashSet.
//   - PlatformAgentCommandPollingWorker.IsTerminal (Agent) - a string
//     literal set, since that worker transacts status as plain strings
//     over HTTP.
//
// Three copies of one rule means adding a status requires three
// coordinated edits, and missing one fails silently in a different
// direction each time: a new terminal status would be re-applied by
// Cloud, expired out from under itself by the timer, and re-executed by
// the Agent. Defined once here, in the same file's namespace as the enum
// it partitions, so all three read from the same source.
public static class AgentCommandStatusExtensions
{
    public static bool IsTerminal(this AgentCommandStatus status) =>
        status is AgentCommandStatus.Succeeded
            or AgentCommandStatus.Failed
            or AgentCommandStatus.Expired
            or AgentCommandStatus.Cancelled;
}
