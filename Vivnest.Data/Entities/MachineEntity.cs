using Azure;
using Azure.Data.Tables;

namespace Vivnest.Core.DataStores.Entities;

// Machine/Agent/AgentInstallation domain model (decision-log.md ADR-053).
// Tenant-scoped like AgentRegistryEntity/DeviceRegistryEntity, not global
// like CapabilityEntity - a physical/virtual host genuinely belongs to one
// tenant/site.
public sealed class MachineEntity : BaseEntity, ITableEntity
{
    // == $"{TenantId}|{SiteId}" - same convention as every other
    // tenant-scoped entity in this codebase.
    public string PartitionKey { get; set; } = default!;

    // == MachineId - caller-chosen (like TenantId/SiteId), not a
    // generated Guid.
    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string Name { get; set; } = default!;

    public string? Hostname { get; set; }

    public string? Description { get; set; }

    // Stored as MachineStatus.ToString() - same enum-on-the-wire
    // convention as CapabilityEntity.CapabilityType.
    public string Status { get; set; } = default!;

    public string? OperatingSystem { get; set; }

    public string? Architecture { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
