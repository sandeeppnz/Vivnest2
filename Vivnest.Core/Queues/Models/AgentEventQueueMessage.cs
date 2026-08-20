namespace Vivnest.Core.Queues.Models;

// Mirrors DeviceEventQueueMessage exactly: generic, because the Cloud-side
// handler refetches the AgentEventEntity and branches on its own EventType
// rather than needing a message subtype per event type. Per ADR-004 a queue
// message only ever carries {PartitionKey, RowKey} anyway.
public class AgentEventQueueMessage : QueueMessage
{
}
