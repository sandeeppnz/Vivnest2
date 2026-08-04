namespace Vivnest.Core.Queues.Models;

// Mirrors RestartCommandQueueMessage exactly - same Cloud-to-Agent shape,
// same reasoning (ADR-004 doesn't apply, there's no persisted row to
// reference). Deliberately no image tag / extra config in the payload yet
// - v1 always deploys ":latest"; carrying arbitrary deploy config through
// the queue message is a real extension point for later, not built until
// something actually needs it.
public sealed record DeployCommandQueueMessage(
    string AgentId,
    DateTime IssuedAtUtc);
