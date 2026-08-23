using Azure;
using Azure.Data.Tables;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Entities;

public sealed class DeviceSnapshotStateEntity : AgentEntity, ITableEntity
{
    public string PartitionKey { get; set; } = default!;

    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string DeviceId { get; set; } = default!;

    public DateTime? LastNotifiedUtc { get; set; }
}
