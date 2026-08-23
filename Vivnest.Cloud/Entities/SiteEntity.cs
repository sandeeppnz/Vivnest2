using Azure;
using Azure.Data.Tables;

namespace Vivnest.Cloud.Entities;

// PartitionKey = TenantId, RowKey = SiteId (not BaseEntity's "{TenantId}|
// {SiteId}" convention - a Site defines that scope, it isn't itself scoped
// by it). This makes "list every Site for Tenant X" a single
// partition-scoped query without a full table scan.
public sealed class SiteEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!;

    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    // Duplicated onto properties (not just PartitionKey/RowKey) so DTO
    // mapping doesn't need to reach into the raw table-entity keys -
    // matches TenantId/SiteId being real, named properties everywhere
    // else in this codebase.
    public string TenantId { get; set; } = default!;

    public string SiteId { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    // Stored as SiteStatus.ToString() - same enum-on-the-wire convention
    // as CapabilityEntity.CapabilityType.
    public string Status { get; set; } = default!;

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
