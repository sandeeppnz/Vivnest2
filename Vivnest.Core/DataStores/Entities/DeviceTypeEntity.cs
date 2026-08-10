using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.DataStores.Entities;

// Admin > Device Types master list (decision-log.md ADR-047). Deliberately
// NOT tenant-scoped (does not extend BaseEntity), same reasoning as
// CapabilityEntity - a device type like "Camera" is a shared concept across
// tenants, not owned by one. PartitionKey is a constant so listing every
// device type is a single cheap partition-scoped query.
//
// Deliberately separate from Vivnest.Core.Enums.DeviceType - that enum is
// the mechanical classification real Agent code branches on
// (CameraCaptureWorker, MotionSensorMonitorService, SmartPlugMonitorService
// are each hardcoded to one value); this master list is admin-managed
// reference data with no code behind new entries until a matching Agent
// capability is actually built. Same "two unrelated concepts, same English
// word" split ADR-042 already established for Capability.
public sealed class DeviceTypeEntity : ITableEntity
{
    public const string PartitionKeyValue = "devicetype";

    public string PartitionKey { get; set; } = PartitionKeyValue;

    // == DeviceTypeId.ToString() - no separate id field, same as
    // CapabilityEntity.
    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string DeviceTypeName { get; set; } = default!;
}
