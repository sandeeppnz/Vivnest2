using Vivnest.Core.ModelRegistry;

namespace Vivnest.Cloud.Api.Dtos;

// Model registry (ADR-124) - see docs/architecture/model-registry-design.md.

public sealed record ModelDto(
    Guid ModelId,
    string Name,
    string? Description,
    string Status,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    IReadOnlyList<ModelVersionDto> Versions);

public sealed record ModelVersionDto(
    int Version,
    string Status,
    string? Notes,
    DateTime UploadedUtc,
    IReadOnlyList<ModelFileEntry> Files);

public sealed record CreateModelRequest(
    string Name,
    string? Description);

public sealed record UpdateModelRequest(
    string Name,
    string? Description,
    string Status);

public sealed record UpdateModelVersionRequest(
    string Status,
    string? Notes);

// One uploaded file, already read out of the multipart form by the
// function layer - the service never touches HttpRequest.
public sealed record ModelFileUpload(
    string FileName,
    byte[] Content);
