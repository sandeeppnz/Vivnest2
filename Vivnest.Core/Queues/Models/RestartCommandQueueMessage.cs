namespace Vivnest.Core.Queues.Models;

// The first Cloud-to-Agent queue message in this codebase - every other
// queue flows Agent-to-Cloud (see decision-log.md ADR-004). Deliberately
// not following ADR-004's {PartitionKey, RowKey}-only shape - that rule
// exists because those messages reference an already-persisted Table
// Storage row; a command has no such row, it *is* the payload. AgentId is
// carried so a future multi-agent deployment sharing this queue could
// filter for messages actually addressed to it - not needed with today's
// single agent, but cheap to include now and expensive to retrofit later.
public sealed record RestartCommandQueueMessage(
    string AgentId,
    DateTime IssuedAtUtc);
