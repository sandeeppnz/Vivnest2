using Azure;
using Azure.Data.Tables;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Entities;

// One immutable version of a model's file set (ADR-124). Never edited
// after creation except for Status (Active/Retired) and Notes - the
// files themselves, their hashes, and the version number are frozen the
// moment the row exists.
public sealed class ModelVersionEntity : BaseEntity, ITableEntity
{
    // == $"{TenantId}|{SiteId}|{ModelId}" - one partition per model, so
    // listing a model's versions is a single partition-scoped query.
    public string PartitionKey { get; set; } = default!;

    // Zero-padded version number ("000001") so lexical RowKey order IS
    // version order - same D6 convention as the config-version rows
    // (ADR-069).
    public string RowKey { get; set; } = default!;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public int Version { get; set; }

    // JSON-serialized ModelFileEntry[] (Vivnest.Core.ModelRegistry) -
    // name, size, SHA-256, IsPrimary per file. Same
    // serialized-collection-in-a-column convention as
    // CapabilityEntity.ConfigurationSchema.
    public string Files { get; set; } = default!;

    // Stored as ModelVersionStatus.ToString() ("Active"/"Retired").
    public string? Status { get; set; }

    public string? Notes { get; set; }

    public DateTime UploadedUtc { get; set; }
}
