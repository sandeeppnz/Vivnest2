namespace Vivnest.Core.Enums;

// Decision-log.md ADR-079 - Phase 9's command lifecycle. Happy path:
// Pending -> Dispatched -> Received -> Executing -> Succeeded. Received/
// Executing are best-effort Agent-reported checkpoints, not required for
// a command to reach a terminal state - RestartAgent in particular has no
// time to report Executing before its process exits (see
// AgentCommandManagementService's heartbeat-correlation completion path).
// Cancelled exists for forward compatibility only - nothing in Phase 9
// transitions a command into it yet.
public enum AgentCommandStatus
{
    Pending,
    Dispatched,
    Received,
    Executing,
    Succeeded,
    Failed,
    Expired,
    Cancelled
}
