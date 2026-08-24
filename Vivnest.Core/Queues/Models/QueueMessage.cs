namespace Vivnest.Core.Queues.Models;

public abstract class QueueMessage
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
}

