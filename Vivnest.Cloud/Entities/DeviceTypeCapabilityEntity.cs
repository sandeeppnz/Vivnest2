using Azure;
using Azure.Data.Tables;

namespace Vivnest.Cloud.Entities;

// Global, not tenant-scoped (does not extend BaseEntity), same reasoning
// as CapabilityDependencyEntity - "Camera supports ObjectDetection" is a
// fact about two pieces of shared reference data, not tenant-owned.
// PartitionKey is a constant so listing every compatibility row is a
// single cheap partition-scoped query.
public sealed class DeviceTypeCapabilityEntity : ITableEntity
{
    public const string PartitionKeyValue = "devicetypecapability";

    public string PartitionKey { get; set; } = PartitionKeyValue;

    // == DeviceTypeCapabilityId.ToString() - no separate id field, same
    // as CapabilityEntity.RowKey.
    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string DeviceTypeId { get; set; } = default!;

    public string CapabilityId { get; set; } = default!;
}
