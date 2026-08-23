using Azure;
using Azure.Data.Tables;

namespace Vivnest.Cloud.Entities;

// Global, not tenant-scoped (does not extend BaseEntity), same reasoning
// as CapabilityEntity/DeviceTypeEntity - a dependency between two shared
// reference-data Capabilities isn't owned by a tenant. PartitionKey is a
// constant so listing every dependency edge is a single cheap
// partition-scoped query, same shape as CapabilityEntity.
public sealed class CapabilityDependencyEntity : ITableEntity
{
    public const string PartitionKeyValue = "capabilitydependency";

    public string PartitionKey { get; set; } = PartitionKeyValue;

    // == DependencyId.ToString() - no separate id field, same as
    // CapabilityEntity.RowKey.
    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string CapabilityId { get; set; } = default!;

    public string DependsOnCapabilityId { get; set; } = default!;

    // Stored as CapabilityDependencyType.ToString().
    public string DependencyType { get; set; } = default!;
}
