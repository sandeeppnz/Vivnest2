using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.DataStores.Entities;

// Admin > Devices pre-registration record (decision-log.md ADR-048) -
// declared identity + descriptive facts, mirrors AgentRegistryEntity
// (ADR-043) exactly. Completely separate from DeviceHeartbeatEntity/
// tblDeviceHeartbeat, which stays populated only by real device
// heartbeats, and from the device-config blobs Program.cs actually reads
// at Agent startup - registering a device here does not configure a real
// device. Tenant-scoped, same reasoning as AgentRegistryEntity.
public sealed class DeviceRegistryEntity : BaseEntity, ITableEntity
{
    // == $"{TenantId}|{SiteId}" - listing "this tenant's registered
    // devices" is one partition-scoped query, not a full table scan.
    public string PartitionKey { get; set; } = default!;

    // == DeviceId - no separate id field, same as AgentRegistryEntity.
    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string Name { get; set; } = default!;

    // Vivnest.Core.DataStores.Entities.DeviceTypeEntity.RowKey reference -
    // a Device Types master-list id (ADR-047), not the fixed
    // Vivnest.Core.Enums.DeviceType enum. Empty means not yet declared -
    // no FK-style existence validation, same convention as
    // AgentRegistryEntity.CapabilityIds.
    public string DeviceTypeId { get; set; } = "";

    // AgentRegistryEntity.RowKey reference - which registered agent is
    // expected to own this device. Empty means not yet assigned. No
    // existence validation, same reasoning.
    public string OwningAgentId { get; set; } = "";

    // Purely descriptive, mirrors DeviceOptions.Location/Brand/Model/
    // Firmware's own doc comment: "never read by any capability's worker
    // to decide behavior." Safe to duplicate here since nothing operational
    // depends on it.
    public string Location { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Model { get; set; } = "";
    public string Firmware { get; set; } = "";

    public bool Enabled { get; set; }

    // Comma-separated Capability master-list ids - same pattern and
    // reasoning as AgentRegistryEntity.CapabilityIds (ADR-046).
    public string CapabilityIds { get; set; } = "";

    // JSON-serialized string->string map (ADR-048) - free-form connection
    // facts that vary by device type (Host, Username, RtspUsername,
    // MACAddress, ChildDeviceId, ...), deliberately NOT a fixed schema
    // since DeviceOptions.Settings' actual shape already differs per
    // DeviceType. Also accepts credentials (Password, RtspPassword, ...)
    // by direct request - ADR-050 reversed ADR-048's original guard
    // against this. Unlike every other credential in this codebase
    // (local-only *.secrets.json files, ADR-038), anything stored here is
    // plain text in tblDeviceRegistry, returned as plain text by the
    // admin API to any caller with a valid tenant x-api-key. Empty object
    // means none set.
    public string Settings { get; set; } = "{}";
}
