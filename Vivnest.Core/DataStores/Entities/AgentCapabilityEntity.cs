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
}
