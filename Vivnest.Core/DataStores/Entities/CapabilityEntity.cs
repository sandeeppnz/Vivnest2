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

    // Additive fields (ADR-062, Phase 5) backing the domain class's
    // schema/defaults/status - old rows deserialize these as null/default,
    // treated the same as "not set yet" (empty schema, version 1, empty
    // defaults, blank Status parsed as Active), same backward-compat
    // reasoning DeviceTypeEntity.Description already established. No
    // migration needed on the 4 real rows that predate this ADR.
    //
    // JSON-serialized CapabilityConfigurationField[] - same
    // string->string-map-as-JSON convention DeviceCapabilityEntity.Settings
    // already uses, just an array instead of a map.
    public string? ConfigurationSchema { get; set; }

    public int ConfigurationSchemaVersion { get; set; } = 1;

    // JSON-serialized string->string map, same shape/convention as
    // DeviceCapabilityEntity.Settings.
    public string? DefaultConfiguration { get; set; }

    // Stored as CapabilityStatus.ToString(). Blank/unparseable treated as
    // Active by the management service - same "blank enum -> default"
    // precedent AgentRegistryManagementService established.
    public string? Status { get; set; }
}
