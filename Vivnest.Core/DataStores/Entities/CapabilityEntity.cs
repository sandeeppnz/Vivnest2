using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.DataStores.Entities;

// Unlike every other entity in this codebase, deliberately NOT tenant-scoped
// (does not extend BaseEntity) - Capability is one global master list shared
// across tenants. PartitionKey is a constant so listing every capability is
// a single cheap partition-scoped query (unlike ApiKeyEntity's
// hash-per-partition scheme, which optimizes point-lookup, not listing).
public sealed class CapabilityEntity : ITableEntity
{
    public const string PartitionKeyValue = "capability";

    public string PartitionKey { get; set; } = PartitionKeyValue;

    // == CapabilityId.ToString() - no separate id field, unlike ApiKeyEntity's
    // KeyId (which only exists there because its PartitionKey is a secret
    // hash, not the id).
    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string CapabilityName { get; set; } = default!;

    // Stored as CapabilityType.ToString() - same convention as
    // DeviceHeartbeatEntity.Source / DeviceEventEntity.ProcessingStatus.
    public string CapabilityType { get; set; } = default!;
}
