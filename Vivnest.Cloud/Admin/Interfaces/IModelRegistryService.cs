using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin.Interfaces;

// Failure carries the operator-facing reason (no primary .onnx, unknown
// model, ...) - same result-not-exception shape as DevicePublishResult.
public sealed record ModelVersionCreationResult(
    ModelVersionDto? Version,
    string? Error);

public interface IModelRegistryService
{
    // Models are listed WITH their versions - the registry is small (a
    // handful of models, a handful of versions each) and every consumer
    // of the list (the admin screen, the assignment editor) wants both.
    Task<IReadOnlyList<ModelDto>> ListAsync(
        TenantContext tenant, CancellationToken cancellationToken = default);

    Task<ModelDto?> GetAsync(
        TenantContext tenant, string modelId, CancellationToken cancellationToken = default);

    Task<ModelDto> CreateAsync(
        TenantContext tenant, string name, string? description,
        CancellationToken cancellationToken = default);

    // Null if the model doesn't exist. Name/Description/Status only -
    // versions are immutable and managed through their own methods.
    Task<ModelDto?> UpdateAsync(
        TenantContext tenant, string modelId, string name, string? description, string status,
        CancellationToken cancellationToken = default);

    // Creates the next version (max existing + 1) from an uploaded file
    // set: writes every blob first, hashing as it goes, then the version
    // row last - a crashed upload leaves orphan blobs, never a row
    // pointing at missing files (ADR-124). Enforces exactly one .onnx
    // (the primary). New versions are born Active.
    Task<ModelVersionCreationResult> CreateVersionAsync(
        TenantContext tenant, string modelId,
        IReadOnlyList<ModelFileUpload> files, string? notes,
        CancellationToken cancellationToken = default);

    // Status (Active/Retired) and Notes only - everything else about a
    // version is frozen at creation. Null if model or version is unknown.
    Task<ModelVersionDto?> UpdateVersionAsync(
        TenantContext tenant, string modelId, int version, string status, string? notes,
        CancellationToken cancellationToken = default);
}
