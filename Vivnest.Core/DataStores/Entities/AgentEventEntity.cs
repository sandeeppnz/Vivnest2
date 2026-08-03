using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.DataStores.Entities;

public sealed class AgentEventEntity : AgentEntity, ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;

    public string RowKey { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }
}
