using Azure;
using Azure.Data.Tables;

namespace Vivnest.Cloud.Entities;

// Tenant is the global root, not itself tenant-scoped - deliberately NOT
// BaseEntity, same reasoning as CapabilityEntity/DeviceTypeEntity: a
// constant PartitionKey makes "list every tenant" a single cheap
// partition-scoped query. RowKey == TenantId, no separate id field.
public sealed class TenantEntity : ITableEntity
{
    public const string PartitionKeyValue = "TENANT";

    public string PartitionKey { get; set; } = PartitionKeyValue;

    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    // Stored as TenantStatus.ToString() - same enum-on-the-wire convention
    // as CapabilityEntity.CapabilityType.
    public string Status { get; set; } = default!;

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
