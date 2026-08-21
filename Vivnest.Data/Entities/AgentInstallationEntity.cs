using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.DataStores.Entities;

// Machine/Agent/AgentInstallation domain model (decision-log.md ADR-053).
// PartitionKey = TenantId|SiteId (not AgentId/MachineId/InstallationId) -
// "list installations for this Agent/Machine" is a partition-scoped scan
// filtered client-side on AgentId/MachineId, same pattern
// AzureTableDeviceEventReader already uses for tenant-wide queries. No
// separate tblMachineAgents relationship table - the relationship is
// derivable from this table alone (spec's own explicit instruction).
public sealed class AgentInstallationEntity : AgentEntity, ITableEntity
{
    // == $"{TenantId}|{SiteId}"
    public string PartitionKey { get; set; } = default!;

    // == InstallationId (generated Guid)
    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string MachineId { get; set; } = default!;

    public string? ContainerId { get; set; }

    public string? ImageName { get; set; }

    public string? ImageVersion { get; set; }

    // Stored as AgentInstallationStatus.ToString().
    public string Status { get; set; } = default!;

    public DateTime InstalledUtc { get; set; }

    public DateTime? RemovedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
