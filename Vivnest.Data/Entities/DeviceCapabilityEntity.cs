using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.DataStores.Entities;

// Device/DeviceType/Capability/DeviceCapability domain model (decision-log.md
// ADR-057). PartitionKey = TenantId|SiteId (not DeviceId) - "list this
// device's capability assignments" is a partition-scoped scan filtered by
// DeviceId client-side, same pattern AgentInstallationEntity already uses
// for AgentId/MachineId. No separate join table beyond this one - the
// Device<->Capability relationship is this table.
public sealed class DeviceCapabilityEntity : BaseEntity, ITableEntity
{
    // == $"{TenantId}|{SiteId}"
    public string PartitionKey { get; set; } = default!;

    // == DeviceCapabilityId (generated Guid)
    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string DeviceId { get; set; } = default!;

    public string CapabilityId { get; set; } = default!;

    // Which Agent executes this capability for this device - may differ
    // from DeviceRegistryEntity.OwningAgentId, see DeviceCapability's own
    // doc comment. Empty means not yet assigned, no FK validation, same
    // convention as OwningAgentId elsewhere.
    public string ExecutingAgentId { get; set; } = "";

    public bool Enabled { get; set; }

    // JSON-serialized string->string map, same convention/reasoning as
    // DeviceRegistryEntity.Settings - free-form per-assignment facts
    // (ROI coordinates, model path, confidence threshold, ...) that vary
    // by Capability, without a new column per possible field.
    public string Settings { get; set; } = "{}";

    // Stored as DeviceCapabilityStatus.ToString().
    public string Status { get; set; } = default!;

    public DateTime AssignedUtc { get; set; }

    public DateTime? RemovedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
