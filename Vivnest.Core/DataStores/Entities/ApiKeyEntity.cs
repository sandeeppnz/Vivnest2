using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.DataStores.Entities;

public sealed class ApiKeyEntity : BaseEntity, ITableEntity
{
    public string PartitionKey { get; set; } = default!;

    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string? Name { get; set; }

    public bool Enabled { get; set; }

    public DateTime CreatedUtc { get; set; }
}
