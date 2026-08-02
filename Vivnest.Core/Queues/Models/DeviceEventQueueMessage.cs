namespace Vivnest.Core.Queues.Models;

// Generic - the Cloud-side handler refetches the DeviceEventEntity and
// branches on its own EventType, rather than needing a message subtype
// per event type (per ADR-004, queue messages only ever carry
// {PartitionKey, RowKey} anyway).
public class DeviceEventQueueMessage : QueueMessage
{
}
