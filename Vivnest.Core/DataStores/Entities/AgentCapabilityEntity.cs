using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.DataStores.Entities;

// Agent's declared capability manifest (decision-log.md ADR-059).
// PartitionKey = TenantId|SiteId (not AgentId), same pattern
// AgentInstallationEntity/DeviceCapabilityEntity already use - "list this
// Agent's declared capabilities" is a partition-scoped scan filtered by
// AgentId client-side.
public sealed class AgentCapabilityEntity : AgentEntity, ITableEntity
{
    // == $"{TenantId}|{SiteId}"
    public string PartitionKey { get; set; } = default!;

    // == AgentCapabilityId (generated Guid)
    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string CapabilityId { get; set; } = default!;

    // Stored as AgentCapabilityStatus.ToString().
    public string Status { get; set; } = default!;

    public DateTime AssignedUtc { get; set; }

    public DateTime? RemovedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    // JSON-serialized string->string map, exactly the convention
    // DeviceCapabilityEntity.Settings uses - per-assignment configuration
    // that varies by Capability, without a column per possible field.
    // Defaults to "{}" rather than null so the only failure a reader has
    // to handle is malformed JSON, not malformed-or-absent.
    //
    // ADR-059 explicitly decided against this ("Status alone covers it").
    // ADR-097 reverses that: an assignment now carries how the capability
    // should be configured on this Agent, not merely that it may run.
    public string Settings { get; set; } = "{}";
}
