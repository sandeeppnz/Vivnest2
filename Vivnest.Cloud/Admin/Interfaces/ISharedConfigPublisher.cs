namespace Vivnest.Cloud.Admin.Interfaces;

// Success carries what was written; failure carries the operator-facing
// reason (missing connection string / encryption key) - same
// result-not-exception shape as DevicePublishResult.
public sealed record SharedConfigPublishResult(
    bool Success,
    string? Error,
    string? Container,
    string? BlobName,
    IReadOnlyList<string>? Sections,
    int SizeBytes);

public interface ISharedConfigPublisher
{
    // Generates shared-config/common-config.json from the canonical
    // in-code defaults (ADR-120): the options classes themselves are
    // serialized, so the published document cannot drift from what the
    // Agent binds. The one per-deployment value - the storage connection
    // string - is embedded AES-GCM-encrypted (enc:v1, ADR-085) under the
    // shared CredentialEncryption key. Overwrites the existing blob;
    // agents pick it up on next restart/refresh.
    Task<SharedConfigPublishResult> PublishAsync(CancellationToken cancellationToken = default);
}
