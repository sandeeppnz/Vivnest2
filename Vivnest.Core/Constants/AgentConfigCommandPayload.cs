namespace Vivnest.Core.Constants;

// Decision-log.md ADR-080 (Phase 9 Pass 2) - the normalized Payload shape
// CommandDispatcher writes onto both RefreshConfiguration and
// ApplyConfiguration commands (overwriting whatever the caller originally
// sent for ApplyConfiguration) - both command types resolve to "the Agent
// should be running configuration version N" by the time they're
// persisted, so the Agent-side handlers and the Cloud-side completion hook
// both read this one shape regardless of which command produced it.
public sealed record AgentConfigCommandPayload(int TargetVersion);
