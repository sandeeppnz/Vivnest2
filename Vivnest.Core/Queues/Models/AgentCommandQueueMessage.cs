namespace Vivnest.Core.Queues.Models;

// Decision-log.md ADR-079 - the delivery envelope for RefreshConfiguration/
// ApplyConfiguration/ExecuteCapability, deliberately thin (matches the
// Phase 9 spec's own example payload): the Agent fetches the full command
// via GET /agents/{agentId}/commands/{commandId} before executing, rather
// than carrying the whole command (including Payload/RequestedBy/etc) on
// the wire twice. RestartAgent stays on its own separate queue/message
// shape (RestartCommandQueueMessage) - see decision-log.md's "one queue
// per consumer, not per command" reasoning; this envelope is for the
// three command types that share one new consumer,
// PlatformAgentCommandPollingWorker.
public sealed record AgentCommandQueueMessage(
    string CommandId,
    string AgentId,
    string CommandType);
