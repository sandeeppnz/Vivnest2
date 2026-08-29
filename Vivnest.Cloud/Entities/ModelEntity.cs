using Azure;
using Azure.Data.Tables;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Entities;

// Model registry catalogue row (ADR-124). Tenant-scoped, unlike
// CapabilityEntity's global master list - models are trained on the
// tenant's own imagery, so they are tenant data, not shared reference
// data.
public sealed class ModelEntity : BaseEntity, ITableEntity
{
    // == $"{TenantId}|{SiteId}" - one partition-scoped query lists a
    // tenant's models, same scheme as AgentRegistryEntity.
    public string PartitionKey { get; set; } = default!;

    // == ModelId (GUID) - no separate id field, same as CapabilityEntity.
    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    // Stored as ModelStatus.ToString() ("Active"/"Retired") - blank
    // treated as Active, same "blank enum -> default" precedent as
    // AgentRegistryManagementService.
    public string? Status { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
