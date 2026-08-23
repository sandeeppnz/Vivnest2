using Azure;
using Azure.Data.Tables;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Entities;

// Admin > Agents pre-registration record (decision-log.md ADR-043) -
// completely separate from AgentHeartbeatEntity/tblAgentHeartbeat, which
// stays populated only by real agent heartbeats. Unlike CapabilityEntity
// (deliberately global), this IS tenant-scoped - an agent identity
// genuinely belongs to one tenant/site, matching ApiKeyEntity's/
// DeviceHeartbeatEntity's convention.
//
// This is also the "Agent" domain concept from the Machine/Agent/
// AgentInstallation spec (ADR-053) - Description/Status/CreatedUtc/
// UpdatedUtc were added directly to this entity rather than standing up a
// separate tblAgents, since the two concepts (a tenant's declared agent
// identities) are the same thing. Deliberately does NOT carry
// CurrentMachineId/CurrentInstallationId - "where is this agent currently
// installed" is answered by querying AgentInstallation
// (GetActiveByAgentAsync), not a denormalized field here that could drift
// out of sync (same reasoning as ADR-030's DeviceSummaryDto.ThumbnailUrl).
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

    public string? Description { get; set; }

    // Stored as AgentStatus.ToString() - same enum-on-the-wire convention
    // as CapabilityEntity.CapabilityType. Added by ADR-053 - defaults to
    // "" on any row that predates this field; treat blank as Active
    // (ToDomain/ToDto parse it that way) rather than requiring a backfill.
    public string Status { get; set; } = "";

    public string FirmwareVersion { get; set; } = default!;

    // Stored as AgentType.ToString() - same enum-on-the-wire convention
    // as CapabilityEntity.CapabilityType.
    public string Type { get; set; } = default!;

    // Added by ADR-053. Default matches DateTime's own default so any row
    // that predates this field reads as an (obviously wrong but harmless)
    // epoch date rather than throwing - same tolerance as Status above.
    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    // Additive (ADR-063) - the real Vivnest.Agent process's own
    // appsettings.json "Agent:AgentId" this admin Agent corresponds to.
    // Blank means not linked yet, same tolerance as Status/CreatedUtc above.
    public string? RuntimeAgentId { get; set; }
}
