using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.DataStores.Entities;

// Admin > Agents pre-registration record (decision-log.md ADR-043) -
// completely separate from AgentHeartbeatEntity/tblAgentHeartbeat, which
// stays populated only by real agent heartbeats. Unlike CapabilityEntity
// (deliberately global), this IS tenant-scoped - an agent identity
// genuinely belongs to one tenant/site, matching ApiKeyEntity's/
// DeviceHeartbeatEntity's convention.
public sealed class AgentRegistryEntity : BaseEntity, ITableEntity
{
    // == $"{TenantId}|{SiteId}" - listing "this tenant's registered
    // agents" is one partition-scoped query, not a full table scan
    // (unlike ApiKeyEntity's hash-based partitioning).
    public string PartitionKey { get; set; } = default!;

    // == AgentId - no separate id field, same as CapabilityEntity.
    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string Name { get; set; } = default!;

    public string FirmwareVersion { get; set; } = default!;

    // Stored as AgentType.ToString() - same enum-on-the-wire convention
    // as CapabilityEntity.CapabilityType.
    public string Type { get; set; } = default!;
}
