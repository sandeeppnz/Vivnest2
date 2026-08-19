using System.Security.Cryptography;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Options;
using Vivnest.Core.Security;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Admin;

// The publish pipeline itself, shared by AgentRuntimeConfigurationPublisher
// and DeviceRuntimeConfigurationPublisher (decision-log.md ADR-069/070).
//
// Those two classes were ~465 and ~510 lines of which the great majority
// was the same algorithm twice: read the state row, compare hashes, claim
// the next version number with an immutable failIfExists blob, repoint the
// manifest, rewrite the legacy flat blob, then update the state row under
// its ETag - retrying the whole cycle on either a 409 (version claimed) or
// a 412 (row moved). Along with it went three verbatim-identical helpers:
// the encryption-key gate (ADR-085), the content hash, and the best-effort
// restart dispatch (ADR-068).
//
// Only three things genuinely differ between the two sides, and those are
// what ConfigurationPublishTarget<TEntity> and the two callbacks on
// WriteVersionAsync carry:
//   - where the blobs live and what the row entity type is (the target),
//   - what the versioned document actually contains (buildVersionJson),
//   - what goes into the legacy flat blob: the Device side writes the same
//     bytes as the version blob, while the Agent side merge-patches its
//     own keys onto whatever is already there so an agent-local section
//     like HomeAssistant survives (buildFlatJson).
public sealed class RuntimeConfigurationWriter<TEntity>
    where TEntity : class, ITableEntity, IConfigurationStateEntity
{
    // Decision-log.md ADR-069 - bounded retry against a concurrent publish
    // (either a blob-name collision on the immutable version blob, or a
    // stale ETag on the metadata row). Not a distributed transaction -
    // explicitly out of scope - just enough that "two Admins publish at
    // once" resolves to two real, ordered versions instead of one silently
    // winning over the other.
    private const int MaxPublishAttempts = 3;

    private readonly IBlobStorageClient _blobClient;
    private readonly IConfigurationStateStore<TEntity> _configurations;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IOptions<CredentialEncryptionOptions> _credentialEncryption;
    private readonly ILogger<RuntimeConfigurationWriter<TEntity>> _logger;

    public RuntimeConfigurationWriter(
        IBlobStorageClient blobClient,
        IConfigurationStateStore<TEntity> configurations,
        ICommandDispatcher commandDispatcher,
        IOptions<CredentialEncryptionOptions> credentialEncryption,
        ILogger<RuntimeConfigurationWriter<TEntity>> logger)
    {
        _blobClient = blobClient;
        _configurations = configurations;
        _commandDispatcher = commandDispatcher;
        _credentialEncryption = credentialEncryption;
        _logger = logger;
    }

    // Decision-log.md ADR-069, spec section 7/8 - the hashed content is
    // deliberately only the subset an Admin actually controls; it excludes
    // PublishedUtc/SchemaVersion/ConfigurationVersion/ConfigurationHash,
    // which change on every publish attempt even when nothing meaningful
    // did. Not a fully canonical form (nested Settings dictionaries
    // serialize in whatever order they were parsed in, not sorted) -
    // stable for repeated hashing of the SAME stored admin data, which is
    // all change-detection actually needs; full canonicalization would be
    // solving a problem that does not exist here.
    public static string ComputeHash<TContent>(TContent content)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(content);

        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    // decision-log.md ADR-085 - a missing/invalid key blocks publish
    // outright (same "Cannot publish: ..." pattern as the callers' own
    // Warnings gate) rather than silently falling back to plaintext - a
    // security control that degrades silently on misconfiguration isn't one.
    public bool TryGetEncryptionKey(out byte[] key, out string? error)
    {
        key = [];
        var configuredKey = _credentialEncryption.Value.Key;

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            error = "Cannot publish: CredentialEncryption:Key is not configured on the Cloud service - sensitive Settings fields cannot be encrypted (decision-log.md ADR-085).";
            return false;
        }

        try
        {
            key = CredentialCipher.ParseKey(configuredKey);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Cannot publish: CredentialEncryption:Key is misconfigured ({ex.Message}).";
            return false;
        }
    }

    // Decision-log.md ADR-070 - the write-version-blob -> overwrite-manifest
    // -> overwrite-legacy-flat-blob -> update-metadata-row retry cycle,
    // shared by PublishAsync (content from the live projection) and
    // RollbackAsync (content from an old version blob, verbatim).
    // bypassNoOpCheck lets a rollback always create a new version even when
    // its content happens to match what is already published - see either
    // publisher's RollbackAsync comment.
    public async Task<VersionWriteResult> WriteVersionAsync(
        TenantContext tenant,
        string runtimeId,
        ConfigurationPublishTarget<TEntity> target,
        string hash,
        bool bypassNoOpCheck,
        Func<int, DateTime, byte[]> buildVersionJson,
        Func<int, DateTime, byte[], Task<byte[]>> buildFlatJson,
        CancellationToken cancellationToken)
    {
        var partitionKey = new SiteScope(tenant.TenantId, tenant.SiteId).PartitionKey;

        // The scoped layout is the one being moved to; the unscoped one is
        // where every already-published blob lives and where an Agent that
        // predates this change still looks. Both are written on every
        // publish for now - see the dual-write note further down.
        var key = new ConfigBlobKey(tenant.TenantId, tenant.SiteId, runtimeId);
        var legacyKey = key.Unscoped();

        for (var attempt = 1; attempt <= MaxPublishAttempts; attempt++)
        {
            var existing = await _configurations.GetAsync(partitionKey, runtimeId, cancellationToken);

            if (!bypassNoOpCheck && existing != null && existing.CurrentHash == hash)
            {
                // Unchanged content must not burn a version number - but it
                // must not block the LAYOUT migration either. Those are
                // different questions, and conflating them meant an entity
                // whose configuration happened to be stable never grew
                // scoped blobs at all: this guard returns before any blob
                // is written. In a settled system that is most entities, so
                // the migration would have quietly covered only whatever
                // happened to change, and removing the dual-write later
                // would have stranded the rest.
                await BackfillScopedLayoutAsync(
                    target, key, legacyKey, existing.CurrentVersion, existing.CurrentHash, cancellationToken);

                return new VersionWriteResult(
                    false, existing.CurrentVersion, $"Configuration unchanged since version {existing.CurrentVersion}.");
            }

            var newVersion = (existing?.CurrentVersion ?? 0) + 1;
            var publishedUtc = DateTime.UtcNow;

            var versionJson = buildVersionJson(newVersion, publishedUtc);

            try
            {
                // Immutable - IfNoneMatch: "*" fails with 409 if this exact
                // version number was already claimed, meaning a concurrent
                // publish beat us to it. Never overwritten once written.
                // Only the SCOPED version blob is written with
                // failIfExists as a race check, because it is the one that
                // decides whether this attempt won. The legacy mirror
                // below copies an already-won version, so a collision
                // there says nothing.
                await _blobClient.UploadAsync(
                    target.ContainerName,
                    target.VersionBlobName(key, newVersion),
                    new MemoryStream(versionJson),
                    failIfExists: true,
                    cancellationToken: cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                _logger.LogWarning(
                    "{EntityKind} {RuntimeId} version {Version} was claimed by a concurrent publish; retrying (attempt {Attempt}/{Max}).",
                    target.EntityKind, runtimeId, newVersion, attempt, MaxPublishAttempts);

                continue;
            }

            // The pointer, not the content - safe to overwrite freely.
            //
            // ConfigurationUri is a full container-relative name, so the
            // scoped manifest points into the scoped layout and the legacy
            // one into the legacy layout. A reader follows whichever
            // manifest it found and never has to know which layout it is
            // looking at.
            var manifest = new ConfigurationManifest(
                newVersion, hash, target.VersionBlobName(key, newVersion), publishedUtc);

            await _blobClient.UploadAsync(
                target.ContainerName,
                target.ManifestBlobName(key),
                new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifest)),
                cancellationToken: cancellationToken);

            // Legacy flat blob (decision-log.md ADR-064 onward) - still
            // written on every publish, unchanged path, now additionally
            // carrying ConfigurationVersion/ConfigurationHash so even an
            // Agent build that only ever reads this path can report them.
            // "Run alongside," never replaced, per the user's own choice.
            var flatJson = await buildFlatJson(newVersion, publishedUtc, versionJson);

            await _blobClient.UploadAsync(
                target.ContainerName,
                target.FlatBlobName(key),
                new MemoryStream(flatJson),
                cancellationToken: cancellationToken);

            // Dual-write to the old unscoped layout. Every Agent currently
            // deployed looks there and nowhere else, so dropping it now
            // would silently strand all of them on their last-known-good
            // config until each was rebuilt and restarted. Three extra
            // uploads per publish; removable once every Agent runs a build
            // that reads the scoped layout. Deliberately not behind a
            // flag - a half-migrated estate is the normal state during a
            // rollout, not an exceptional one.
            if (!key.IsUnscoped)
            {
                var legacyManifest = new ConfigurationManifest(
                    newVersion, hash, target.VersionBlobName(legacyKey, newVersion), publishedUtc);

                await MirrorVersionBlobAsync(
                    target.ContainerName,
                    target.VersionBlobName(legacyKey, newVersion),
                    versionJson,
                    cancellationToken);

                await _blobClient.UploadAsync(
                    target.ContainerName,
                    target.ManifestBlobName(legacyKey),
                    new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(legacyManifest)),
                    cancellationToken: cancellationToken);

                await _blobClient.UploadAsync(
                    target.ContainerName,
                    target.FlatBlobName(legacyKey),
                    new MemoryStream(flatJson),
                    cancellationToken: cancellationToken);
            }

            var entity = target.CreateStateRow(new ConfigurationStateRow(
                partitionKey, runtimeId, tenant.TenantId, tenant.SiteId,
                newVersion, hash, publishedUtc, existing?.ETag ?? default));

            try
            {
                if (existing == null)
                    await _configurations.UpsertAsync(entity, cancellationToken);
                else
                    await _configurations.UpdateAsync(entity, cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                // A concurrent publish updated the metadata row between our
                // read and write - the version blob we just wrote
                // (newVersion) is left in place, immutable and orphaned
                // (never referenced by any manifest), a deliberate,
                // tolerable cost for correctness over perfectly gap-free
                // version numbers - see MaxPublishAttempts' own comment.
                // Retry from a fresh read.
                _logger.LogWarning(
                    "{EntityKind} {RuntimeId} configuration metadata was updated concurrently; retrying (attempt {Attempt}/{Max}).",
                    target.EntityKind, runtimeId, attempt, MaxPublishAttempts);

                continue;
            }

            return new VersionWriteResult(true, newVersion, null);
        }

        return new VersionWriteResult(false, -1, "Concurrent publish detected, please retry.");
    }

    // Copies an already-published version from the legacy layout into the
    // scoped one, creating no new version and restarting nothing.
    //
    // Runs only on the no-op path, and only when the scoped manifest is
    // absent, so it is a one-time backfill per entity: re-running a
    // republish over an already-migrated estate costs one extra read each
    // and writes nothing.
    //
    // The manifest is REBUILT rather than copied. ConfigurationUri is a
    // full container-relative name, so a verbatim copy would leave the
    // scoped manifest pointing back into the legacy layout - and deleting
    // the legacy blobs later would then break exactly the entities this
    // exists to rescue.
    private async Task BackfillScopedLayoutAsync(
        ConfigurationPublishTarget<TEntity> target,
        ConfigBlobKey key,
        ConfigBlobKey legacyKey,
        int version,
        string hash,
        CancellationToken cancellationToken)
    {
        if (key.IsUnscoped)
            return;

        if (await TryDownloadAsync(target.ContainerName, target.ManifestBlobName(key), cancellationToken) != null)
            return;

        var versionJson = await TryDownloadAsync(
            target.ContainerName, target.VersionBlobName(legacyKey, version), cancellationToken);

        if (versionJson == null)
        {
            // Nothing to copy from. A metadata row claiming a version with
            // no blob behind it is already broken in a way a backfill
            // cannot fix, and throwing here would turn a harmless no-op
            // into a failed publish.
            _logger.LogWarning(
                "{EntityKind} {RuntimeId}: cannot backfill the scoped layout, legacy version {Version} blob is missing.",
                target.EntityKind, key.RuntimeId, version);

            return;
        }

        await MirrorVersionBlobAsync(
            target.ContainerName, target.VersionBlobName(key, version), versionJson, cancellationToken);

        var flatJson = await TryDownloadAsync(
            target.ContainerName, target.FlatBlobName(legacyKey), cancellationToken);

        if (flatJson != null)
        {
            await _blobClient.UploadAsync(
                target.ContainerName, target.FlatBlobName(key),
                new MemoryStream(flatJson), cancellationToken: cancellationToken);
        }

        // PublishedUtc is the only field not carried over verbatim - the
        // legacy manifest's own value is the honest one, but re-reading it
        // just to copy one timestamp costs another round trip for something
        // no reader treats as authoritative (the version blob carries its
        // own PublishedUtc, and that is what the Agent reports).
        var manifest = new ConfigurationManifest(
            version, hash, target.VersionBlobName(key, version), DateTime.UtcNow);

        await _blobClient.UploadAsync(
            target.ContainerName, target.ManifestBlobName(key),
            new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifest)),
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "{EntityKind} {RuntimeId}: backfilled version {Version} into the scoped layout.",
            target.EntityKind, key.RuntimeId, version);
    }

    private async Task<byte[]?> TryDownloadAsync(
        string containerName, string blobName, CancellationToken cancellationToken)
    {
        try
        {
            return await _blobClient.DownloadAsync(containerName, blobName, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    // The legacy mirror of an immutable version blob. Written with
    // failIfExists so an existing blob is never overwritten - version
    // blobs are immutable in both layouts - but a 409 here only means the
    // mirror is already there, which is not a reason to fail or retry a
    // publish that has already succeeded.
    private async Task MirrorVersionBlobAsync(
        string containerName, string blobName, byte[] content, CancellationToken cancellationToken)
    {
        try
        {
            await _blobClient.UploadAsync(
                containerName, blobName, new MemoryStream(content),
                failIfExists: true, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
        }
    }

    // Decision-log.md ADR-068 - closes the loop for an online owning agent
    // automatically; an offline one just picks up the new blob at its next
    // startup regardless, same as always. Best-effort: a queue hiccup must
    // never fail a publish that already succeeded, and this agent may not
    // even be running yet (nothing to restart) or may have no
    // RuntimeAgentId mapped (a device's OwningAgentId null) - both
    // silently skipped, not errors.
    // Routed through ICommandDispatcher rather than IAgentCommandPublisher
    // directly, so a publish-triggered restart is a tracked tblAgentCommands
    // row like every other restart since ADR-079 - this used to be the one
    // restart path that left no trace in command history.
    //
    // Two dispatcher outcomes exist that the raw enqueue didn't have, and
    // both are logged rather than surfaced, keeping this best-effort: a
    // null result (the owning Agent isn't resolvable for this tenant, i.e.
    // no heartbeat, so there's no running container to restart anyway),
    // and an AGENT_BUSY rejection (another disruptive command is already
    // in flight - that one will pick up this config when it restarts).
    public async Task TryEnqueueRestartAsync(
        TenantContext tenant,
        string? runtimeAgentId,
        string requestedBy,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runtimeAgentId))
            return;

        try
        {
            var command = await _commandDispatcher.DispatchAsync(
                tenant,
                AgentCommandTypes.RestartAgent,
                runtimeAgentId,
                requestedBy,
                cancellationToken: cancellationToken);

            if (command == null)
            {
                _logger.LogInformation(
                    "No restart dispatched for agent {RuntimeAgentId} after publish - not resolvable for this tenant (no heartbeat yet).",
                    runtimeAgentId);
            }
            else if (command.ErrorCode != null)
            {
                _logger.LogWarning(
                    "Restart command {CommandId} for agent {RuntimeAgentId} rejected after publish: {ErrorCode} {ErrorMessage}",
                    command.CommandId, runtimeAgentId, command.ErrorCode, command.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Failed to dispatch restart command for agent {RuntimeAgentId} after publish.", runtimeAgentId);
        }
    }
}

// Everything about a publish that is fixed per side rather than per call:
// which container and blob names the versioned/manifest/legacy-flat blobs
// use, which word to log, and how to build the state row. Declared once as
// a static readonly field on each publisher.
//
// CreateStateRow is a factory rather than the writer newing up a TEntity
// itself because BaseEntity declares TenantId/SiteId as `required init`,
// which an object initializer can satisfy and a generic `new()` cannot.
public sealed record ConfigurationPublishTarget<TEntity>(
    string EntityKind,
    string ContainerName,
    Func<ConfigBlobKey, int, string> VersionBlobName,
    Func<ConfigBlobKey, string> ManifestBlobName,
    Func<ConfigBlobKey, string> FlatBlobName,
    Func<ConfigurationStateRow, TEntity> CreateStateRow)
    where TEntity : class, ITableEntity, IConfigurationStateEntity;

// The state-row values the writer computed, handed to CreateStateRow. ETag
// is the one read back off the existing row - passing it through to the new
// entity is what makes UpdateAsync's 412 meaningful.
public readonly record struct ConfigurationStateRow(
    string PartitionKey,
    string RowKey,
    string TenantId,
    string SiteId,
    int Version,
    string Hash,
    DateTime PublishedUtc,
    ETag ETag);

// Decision-log.md ADR-070 - the outcome of a single WriteVersionAsync
// attempt cycle. Success is false both for the ordinary "nothing changed"
// no-op and for retry exhaustion - both are well-formed outcomes for a
// caller to render via Reason, not exceptions.
public readonly record struct VersionWriteResult(bool Success, int Version, string? Reason);
