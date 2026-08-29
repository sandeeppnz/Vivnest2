using System.Security.Cryptography;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Entities;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Constants;
using Vivnest.Core.ModelRegistry;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Admin;

// Model registry (ADR-124) - see docs/architecture/model-registry-design.md
// for the full semantics this implements: immutable numbered versions,
// atomic file sets with exactly one primary .onnx, server-side SHA-256
// as the authority the agent verifies against.
public sealed class ModelRegistryService : IModelRegistryService
{
    private readonly IModelStore _models;
    private readonly IModelVersionStore _versions;
    private readonly IBlobStorageClient _blobClient;

    public ModelRegistryService(
        IModelStore models,
        IModelVersionStore versions,
        IBlobStorageClient blobClient)
    {
        _models = models;
        _versions = versions;
        _blobClient = blobClient;
    }

    public async Task<IReadOnlyList<ModelDto>> ListAsync(
        TenantContext tenant, CancellationToken cancellationToken = default)
    {
        var rows = await _models.ListAsync(tenant.TenantId, tenant.SiteId, cancellationToken);
        var result = new List<ModelDto>(rows.Count);

        foreach (var row in rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            result.Add(await ToDtoAsync(tenant, row, cancellationToken));

        return result;
    }

    public async Task<ModelDto?> GetAsync(
        TenantContext tenant, string modelId, CancellationToken cancellationToken = default)
    {
        var row = await _models.GetAsync(tenant.TenantId, tenant.SiteId, modelId, cancellationToken);

        return row == null ? null : await ToDtoAsync(tenant, row, cancellationToken);
    }

    public async Task<ModelDto> CreateAsync(
        TenantContext tenant, string name, string? description,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var entity = new ModelEntity
        {
            PartitionKey = $"{tenant.TenantId}|{tenant.SiteId}",
            RowKey = Guid.NewGuid().ToString(),
            TenantId = tenant.TenantId,
            SiteId = tenant.SiteId,
            Name = name,
            Description = description,
            Status = ModelStatus.Active.ToString(),
            CreatedUtc = now,
            UpdatedUtc = now,
        };

        await _models.CreateAsync(entity, cancellationToken);

        return await ToDtoAsync(tenant, entity, cancellationToken);
    }

    public async Task<ModelDto?> UpdateAsync(
        TenantContext tenant, string modelId, string name, string? description, string status,
        CancellationToken cancellationToken = default)
    {
        var entity = await _models.GetAsync(tenant.TenantId, tenant.SiteId, modelId, cancellationToken);

        if (entity == null)
            return null;

        entity.Name = name;
        entity.Description = description;
        entity.Status = status;
        entity.UpdatedUtc = DateTime.UtcNow;

        await _models.UpdateAsync(entity, cancellationToken);

        return await ToDtoAsync(tenant, entity, cancellationToken);
    }

    public async Task<ModelVersionCreationResult> CreateVersionAsync(
        TenantContext tenant, string modelId,
        IReadOnlyList<ModelFileUpload> files, string? notes,
        CancellationToken cancellationToken = default)
    {
        var model = await _models.GetAsync(tenant.TenantId, tenant.SiteId, modelId, cancellationToken);

        if (model == null)
            return new ModelVersionCreationResult(null, "Model not found.");

        if (files.Count == 0)
            return new ModelVersionCreationResult(null, "At least one file is required.");

        // Exactly one .onnx: it is the file the inference session opens;
        // companions (.onnx.data etc.) ride alongside it. Enforced here so
        // the two-file external-data gotcha that broke the first High-agent
        // install is impossible to recreate one file at a time.
        var primaryCount = files.Count(f =>
            f.FileName.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase));

        if (primaryCount != 1)
        {
            return new ModelVersionCreationResult(
                null, $"A version must contain exactly one .onnx file (got {primaryCount}).");
        }

        if (files.Select(f => f.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            return new ModelVersionCreationResult(null, "Duplicate file names in the upload.");

        var existing = await _versions.ListAsync(
            tenant.TenantId, tenant.SiteId, modelId, cancellationToken);

        var version = existing.Count == 0 ? 1 : existing.Max(v => v.Version) + 1;

        // Blobs first, row last (ADR-124): a crash mid-upload leaves
        // harmless orphan blobs, never a row pointing at missing files.
        var manifest = new List<ModelFileEntry>(files.Count);

        foreach (var file in files)
        {
            using var stream = new MemoryStream(file.Content);

            await _blobClient.UploadAsync(
                ModelBlob.ContainerName,
                ModelBlob.BlobName(modelId, version, file.FileName),
                stream,
                httpHeaders: null,
                failIfExists: false,
                cancellationToken);

            manifest.Add(new ModelFileEntry(
                file.FileName,
                file.Content.LongLength,
                Convert.ToHexString(SHA256.HashData(file.Content)).ToLowerInvariant(),
                IsPrimary: file.FileName.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)));
        }

        var entity = new ModelVersionEntity
        {
            PartitionKey = $"{tenant.TenantId}|{tenant.SiteId}|{modelId}",
            RowKey = version.ToString("D6"),
            TenantId = tenant.TenantId,
            SiteId = tenant.SiteId,
            Version = version,
            Files = ModelFileEntry.Serialize(manifest),
            Status = ModelVersionStatus.Active.ToString(),
            Notes = notes,
            UploadedUtc = DateTime.UtcNow,
        };

        await _versions.CreateAsync(entity, cancellationToken);

        return new ModelVersionCreationResult(ToVersionDto(entity), null);
    }

    public async Task<ModelVersionDto?> UpdateVersionAsync(
        TenantContext tenant, string modelId, int version, string status, string? notes,
        CancellationToken cancellationToken = default)
    {
        var entity = await _versions.GetAsync(
            tenant.TenantId, tenant.SiteId, modelId, version, cancellationToken);

        if (entity == null)
            return null;

        entity.Status = status;
        entity.Notes = notes;

        await _versions.UpdateAsync(entity, cancellationToken);

        return ToVersionDto(entity);
    }

    private async Task<ModelDto> ToDtoAsync(
        TenantContext tenant, ModelEntity entity, CancellationToken cancellationToken)
    {
        var versionRows = await _versions.ListAsync(
            tenant.TenantId, tenant.SiteId, entity.RowKey, cancellationToken);

        return new ModelDto(
            Guid.Parse(entity.RowKey),
            entity.Name,
            entity.Description,
            string.IsNullOrWhiteSpace(entity.Status) ? ModelStatus.Active.ToString() : entity.Status,
            entity.CreatedUtc,
            entity.UpdatedUtc,
            versionRows.OrderByDescending(v => v.Version).Select(ToVersionDto).ToList());
    }

    private static ModelVersionDto ToVersionDto(ModelVersionEntity entity) =>
        new(
            entity.Version,
            string.IsNullOrWhiteSpace(entity.Status) ? ModelVersionStatus.Active.ToString() : entity.Status,
            entity.Notes,
            entity.UploadedUtc,
            ModelFileEntry.TryDeserialize(entity.Files) ?? []);
}

// "Retired, not deleted" - same lifecycle doctrine as DeviceStatus:
// a retired model/version keeps its identity (published configs may
// still reference it) but drops out of default resolution.
public enum ModelStatus
{
    Active,
    Retired
}

public enum ModelVersionStatus
{
    Active,
    Retired
}
