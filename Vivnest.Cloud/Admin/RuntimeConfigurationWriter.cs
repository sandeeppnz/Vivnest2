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
using Vivnest.Core.Options;
using Vivnest.Core.Security;
using Vivnest.Core.Storage;
using Vivnest.Domain.Agents;
using Vivnest.Domain.Devices;
using Vivnest.Domain.Sites;

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

        var key = new ConfigBlobKey(tenant.TenantId, tenant.SiteId, runtimeId);

        for (var attempt = 1; attempt <= MaxPublishAttempts; attempt++)
        {
            var existing = await _configurations.GetAsync(partitionKey, runtimeId, cancellationToken);

            if (!bypassNoOpCheck && existing != null && existing.CurrentHash == hash)
            {
                // Unchanged content must not burn a version number.
                //
                // This guard used to also drive the scoped-layout backfill,
                // because returning here skips every blob write - so an
                // entity whose configuration happened to be stable would
                // otherwise never have grown scoped blobs at all. The
                // backfill went with the legacy layout (ADR-091); the
                // no-op guard is back to being only what its name says.
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
                // publish beat us to it. Never overwritten once written,
                // and the 409 is the race check that decides whether this
                // attempt won.
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
