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
using static Vivnest.Core.Constants.RuntimeConfigurationSchemaVersions;

namespace Vivnest.Cloud.Admin;

public sealed class DeviceRuntimeConfigurationPublisher : IDeviceRuntimeConfigurationPublisher
{
    // No naming policy - every real config file in this codebase
    // (common-config.json, appsettings.json, hand-authored device-config/
    // agent-config blobs) uses PascalCase field names matching the C#
    // Options classes exactly, so the wire record types below are
    // deliberately declared with PascalCase properties and serialized
    // with System.Text.Json's default (as-declared) naming.
    private readonly IDeviceRuntimeConfigurationProjector _projector;
    private readonly AzureBlobStorageClient _blobClient;
    private readonly AzureTableStore<DeviceEventEntity> _deviceEvents;
    private readonly AzureTableStore<DeviceConfigurationEntity> _deviceConfigurations;
    private readonly IAgentCommandPublisher _agentCommands;
    private readonly IOptions<CredentialEncryptionOptions> _credentialEncryption;
    private readonly ILogger<DeviceRuntimeConfigurationPublisher> _logger;

    // Decision-log.md ADR-069 - bounded retry against a concurrent publish
    // (either a blob-name collision on the immutable version blob, or a
    // stale ETag on the metadata row). Not a distributed transaction -
    // explicitly out of scope - just enough that "two Admins publish at
    // once" resolves to two real, ordered versions instead of one
    // silently winning over the other.
    private const int MaxPublishAttempts = 3;

    public DeviceRuntimeConfigurationPublisher(
        IDeviceRuntimeConfigurationProjector projector,
        AzureBlobStorageClient blobClient,
        TableServiceClient tableServiceClient,
        IOptions<TablesOptions> tablesOptions,
        IAgentCommandPublisher agentCommands,
        IOptions<CredentialEncryptionOptions> credentialEncryption,
        ILogger<DeviceRuntimeConfigurationPublisher> logger)
    {
        _projector = projector;
        _blobClient = blobClient;
        _deviceEvents = new AzureTableStore<DeviceEventEntity>(tableServiceClient, tablesOptions.Value.DeviceEvents);
        _deviceConfigurations = new AzureTableStore<DeviceConfigurationEntity>(
            tableServiceClient, tablesOptions.Value.DeviceConfiguration);
        _agentCommands = agentCommands;
        _credentialEncryption = credentialEncryption;
        _logger = logger;
    }

    public async Task<DevicePublishResult?> PublishAsync(
        TenantContext tenant,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var document = await _projector.ProjectAsync(tenant, deviceId, cancellationToken);

        if (document == null)
            return null;

        if (document.Warnings.Count > 0)
        {
            return new DevicePublishResult(
                false, document, $"Cannot publish: {string.Join(" ", document.Warnings)}");
        }

        // Warnings empty guarantees this - DeviceRuntimeConfigurationProjector
        // only ever returns a null DeviceId alongside a Warnings entry.
        var runtimeDeviceId = document.DeviceId!;

        if (!TryGetEncryptionKey(out var encryptionKey, out var keyError))
            return new DevicePublishResult(false, document, keyError);

        // decision-log.md ADR-085 - credential-shaped keys (RtspPassword etc.)
        // are still published, but as ciphertext under the shared
        // CredentialEncryption key rather than plaintext (ADR-084) or
        // stripped out entirely (ADR-064/038).
        var connection = CredentialCipher.EncryptFields(document.Settings, encryptionKey);

        var capabilities = document.Capabilities
            .Select(c => c with { Settings = CredentialCipher.EncryptFields(c.Settings, encryptionKey) })
            .ToList();

        var deviceSection = new DeviceRuntimeConfigWireDeviceSection(
            document.Name,
            document.Type,
            document.Enabled,
            document.Location,
            document.Brand,
            document.Model,
            document.Firmware,
            connection);

        // Hashed content deliberately excludes PublishedUtc/SchemaVersion/
        // ConfigurationVersion/ConfigurationHash themselves - those change
        // on every publish attempt even when nothing an Admin actually
        // controls did, which would defeat the whole point of comparing
        // hashes (decision-log.md ADR-069, spec section 7/8). Not a fully
        // canonical form (nested Settings dictionaries serialize in
        // whatever order they were parsed in, not sorted) - stable for
        // repeated hashing of the SAME stored admin data, which is all
        // change-detection actually needs; full canonicalization would be
        // solving a problem that doesn't exist here.
        var hash = ComputeHash(new DeviceConfigHashableContent(deviceSection, document.OwningAgentId, capabilities));

        var result = await WriteVersionAsync(
            tenant, runtimeDeviceId, deviceSection, document.OwningAgentId, capabilities, hash,
            bypassNoOpCheck: false, cancellationToken);

        if (!result.Success)
            return new DevicePublishResult(false, document, result.Reason);

        await WriteAuditEventAsync(
            tenant, deviceId, runtimeDeviceId, DeviceEventTypes.ConfigPublished,
            new { runtimeDeviceId }, cancellationToken);
        await TryEnqueueRestartAsync(document.OwningAgentId, cancellationToken);

        return new DevicePublishResult(true, document, null);
    }

    // Decision-log.md ADR-070 - republishes an old immutable version's
    // content verbatim as a brand-new version, never mutating the old
    // blob (spec section 21). Deliberately does NOT re-project from live
    // Admin state the way PublishAsync does - the whole point of a
    // rollback is to reproduce exactly what version `targetVersion`
    // contained, even if today's live Admin settings have since drifted
    // in ways unrelated to the bad version being rolled back from.
    public async Task<DevicePublishResult?> RollbackAsync(
        TenantContext tenant,
        string deviceId,
        int targetVersion,
        CancellationToken cancellationToken = default)
    {
        // Reuses the projector purely to resolve deviceId -> runtimeDeviceId
        // (and its existing Warnings gate - an unresolvable device can't be
        // rolled back either) - the projected content itself is discarded
        // below in favor of the old version blob's own content.
        var document = await _projector.ProjectAsync(tenant, deviceId, cancellationToken);

        if (document == null)
            return null;

        if (document.Warnings.Count > 0)
        {
            return new DevicePublishResult(
                false, document, $"Cannot roll back: {string.Join(" ", document.Warnings)}");
        }

        var runtimeDeviceId = document.DeviceId!;

        DeviceRuntimeConfigWireDocument targetDocument;

        try
        {
            var targetBytes = await _blobClient.DownloadAsync(
                DeviceConfigBlob.ContainerName,
                DeviceConfigBlob.VersionBlobName(runtimeDeviceId, targetVersion),
                cancellationToken);

            targetDocument = JsonSerializer.Deserialize<DeviceRuntimeConfigWireDocument>(targetBytes)
                ?? throw new JsonException("Version blob deserialized to null.");
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new DevicePublishResult(false, document, $"Version {targetVersion} does not exist for this device.");
        }

        // Always creates a new version, bypassing the hash no-op guard -
        // a deliberate rollback is a real event worth recording in the
        // audit trail even if content happens to already match what's
        // currently published (spec section 21's own "create a new
        // desired version 20" framing).
        var result = await WriteVersionAsync(
            tenant, runtimeDeviceId, targetDocument.Device, targetDocument.OwningAgentId,
            targetDocument.Capabilities, targetDocument.ConfigurationHash,
            bypassNoOpCheck: true, cancellationToken);

        if (!result.Success)
            return new DevicePublishResult(false, document, result.Reason);

        await WriteAuditEventAsync(
            tenant, deviceId, runtimeDeviceId, DeviceEventTypes.ConfigRolledBack,
            new { runtimeDeviceId, rolledBackFromVersion = targetVersion, newVersion = result.Version },
            cancellationToken);
        await TryEnqueueRestartAsync(targetDocument.OwningAgentId, cancellationToken);

        // Reflects what was actually just written (the rolled-back
        // content), not today's live Admin projection - see the method
        // comment above.
        var resultDocument = document with
        {
            Name = targetDocument.Device.Name,
            Type = targetDocument.Device.Type,
            Enabled = targetDocument.Device.Enabled,
            Location = targetDocument.Device.Location,
            Brand = targetDocument.Device.Brand,
            Model = targetDocument.Device.Model,
            Firmware = targetDocument.Device.Firmware,
            OwningAgentId = targetDocument.OwningAgentId,
            Settings = targetDocument.Device.Connection,
            Capabilities = targetDocument.Capabilities,
            Warnings = Array.Empty<string>(),
        };

        return new DevicePublishResult(true, resultDocument, null);
    }

    // Decision-log.md ADR-070 - the write-version-blob -> overwrite-manifest
    // -> overwrite-legacy-flat-blob -> update-metadata-row retry cycle,
    // shared by PublishAsync (content from the live projection) and
    // RollbackAsync (content from an old version blob, verbatim).
    // bypassNoOpCheck lets a rollback always create a new version even when
    // its content happens to match what's already published - see
    // RollbackAsync's own comment.
    private async Task<VersionWriteResult> WriteVersionAsync(
        TenantContext tenant,
        string runtimeDeviceId,
        DeviceRuntimeConfigWireDeviceSection deviceSection,
        string? owningAgentId,
        IReadOnlyList<Vivnest.Cloud.Api.Dtos.CapabilityDocumentEntryDto> capabilities,
        string hash,
        bool bypassNoOpCheck,
        CancellationToken cancellationToken)
    {
        var partitionKey = new SiteScope(tenant.TenantId, tenant.SiteId).PartitionKey;

        for (var attempt = 1; attempt <= MaxPublishAttempts; attempt++)
        {
            var existing = await _deviceConfigurations.GetAsync(partitionKey, runtimeDeviceId, cancellationToken);

            if (!bypassNoOpCheck && existing != null && existing.CurrentHash == hash)
            {
                return new VersionWriteResult(
                    false, existing.CurrentVersion, $"Configuration unchanged since version {existing.CurrentVersion}.");
            }

            var newVersion = (existing?.CurrentVersion ?? 0) + 1;
            var publishedUtc = DateTime.UtcNow;

            var wireDocument = new DeviceRuntimeConfigWireDocument(
                runtimeDeviceId, deviceSection, owningAgentId, capabilities,
                publishedUtc, CurrentDeviceSchemaVersion, newVersion, hash);

            var json = JsonSerializer.SerializeToUtf8Bytes(wireDocument);

            try
            {
                // Immutable - IfNoneMatch: "*" fails with 409 if this exact
                // version number was already claimed, meaning a concurrent
                // publish beat us to it. Never overwritten once written.
                await _blobClient.UploadAsync(
                    DeviceConfigBlob.ContainerName,
                    DeviceConfigBlob.VersionBlobName(runtimeDeviceId, newVersion),
                    new MemoryStream(json),
                    failIfExists: true,
                    cancellationToken: cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                _logger.LogWarning(
                    "Device {RuntimeDeviceId} version {Version} was claimed by a concurrent publish; retrying (attempt {Attempt}/{Max}).",
                    runtimeDeviceId, newVersion, attempt, MaxPublishAttempts);

                continue;
            }

            // The pointer, not the content - safe to overwrite freely.
            var manifest = new ConfigurationManifest(
                newVersion, hash, DeviceConfigBlob.VersionBlobName(runtimeDeviceId, newVersion), publishedUtc);

            await _blobClient.UploadAsync(
                DeviceConfigBlob.ContainerName,
                DeviceConfigBlob.ManifestBlobName(runtimeDeviceId),
                new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifest)),
                cancellationToken: cancellationToken);

            // Legacy flat blob (decision-log.md ADR-064 onward) - still
            // written on every publish, unchanged path, now additionally
            // carrying ConfigurationVersion/ConfigurationHash so even an
            // Agent build that only ever reads this path can report them.
            // "Run alongside," never replaced, per the user's own choice.
            await _blobClient.UploadAsync(
                DeviceConfigBlob.ContainerName,
                DeviceConfigBlob.BlobName(runtimeDeviceId),
                new MemoryStream(json),
                cancellationToken: cancellationToken);

            var entity = new DeviceConfigurationEntity
            {
                PartitionKey = partitionKey,
                RowKey = runtimeDeviceId,
                TenantId = tenant.TenantId,
                SiteId = tenant.SiteId,
                CurrentVersion = newVersion,
                CurrentHash = hash,
                PublishedUtc = publishedUtc,
                ETag = existing?.ETag ?? default
            };

            try
            {
                if (existing == null)
                    await _deviceConfigurations.UpsertAsync(entity, cancellationToken);
                else
                    await _deviceConfigurations.UpdateAsync(entity, cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                // A concurrent publish updated the metadata row between our
                // read and write - the version blob we just wrote (newVersion)
                // is left in place, immutable and orphaned (never referenced
                // by any manifest), a deliberate, tolerable cost for
                // correctness over perfectly gap-free version numbers - see
                // MaxPublishAttempts' own comment. Retry from a fresh read.
                _logger.LogWarning(
                    "Device {RuntimeDeviceId} configuration metadata was updated concurrently; retrying (attempt {Attempt}/{Max}).",
                    runtimeDeviceId, attempt, MaxPublishAttempts);

                continue;
            }

            return new VersionWriteResult(true, newVersion, null);
        }

        return new VersionWriteResult(false, -1, "Concurrent publish detected, please retry.");
    }

    private static string ComputeHash(DeviceConfigHashableContent content)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(content);

        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    // decision-log.md ADR-085 - a missing/invalid key blocks publish outright
    // (same "Cannot publish: ..." pattern as the Warnings gate above) rather
    // than silently falling back to plaintext - a security control that
    // degrades silently on misconfiguration isn't one.
    private bool TryGetEncryptionKey(out byte[] key, out string? error)
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

    // Decision-log.md ADR-068 - closes the loop for an online owning
    // agent automatically; an offline one just picks up the new blob at
    // its next startup regardless, same as always. Best-effort: a queue
    // hiccup must never fail a publish that already succeeded, and this
    // agent may not even be running yet (nothing to restart) or may have
    // no RuntimeAgentId mapped (OwningAgentId null) - both silently
    // skipped, not errors.
    private async Task TryEnqueueRestartAsync(string? runtimeAgentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runtimeAgentId))
            return;

        try
        {
            await _agentCommands.PublishRestartCommandAsync(runtimeAgentId, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "Failed to enqueue restart command for agent {RuntimeAgentId} after publish.", runtimeAgentId);
        }
    }

    private async Task WriteAuditEventAsync(
        TenantContext tenant,
        string deviceId,
        string runtimeDeviceId,
        string eventType,
        object payload,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        await _deviceEvents.UpsertAsync(
            new DeviceEventEntity
            {
                PartitionKey = deviceId,
                RowKey = $"{now:yyyyMMddHHmmssfff}-{Guid.NewGuid()}",
                TenantId = tenant.TenantId,
                SiteId = tenant.SiteId,
                AgentId = "",
                DeviceId = deviceId,
                DeviceType = "",
                EventType = eventType,
                Severity = "Info",
                OccurredAtUtc = now,
                Payload = JsonSerializer.Serialize(payload)
            },
            cancellationToken);
    }
}

// Decision-log.md ADR-070 - the outcome of a single WriteVersionAsync
// attempt cycle. Success is false both for the ordinary "nothing changed"
// no-op and for retry exhaustion - both are well-formed outcomes for a
// caller to render via Reason, not exceptions.
internal readonly record struct VersionWriteResult(bool Success, int Version, string? Reason);

// The real device-config/{runtimeDeviceId}.json shape (decision-log.md
// ADR-064) - distinct from DeviceRuntimeConfigurationDocumentDto, which is
// the flat API-preview shape. This nests device{} and omits Warnings
// (meaningless once actually published - Warnings is always empty by the
// time a write happens, per the hard gate above), since only this shape
// is ever written to Blob Storage.
internal sealed record DeviceRuntimeConfigWireDocument(
    string RuntimeDeviceId,
    DeviceRuntimeConfigWireDeviceSection Device,
    string? OwningAgentId,
    IReadOnlyList<Vivnest.Cloud.Api.Dtos.CapabilityDocumentEntryDto> Capabilities,
    // Phase 6C / decision-log.md ADR-065 - stamped at write time, no
    // shared counter to manage; "newer wins" is just a timestamp compare.
    // Captured by the Agent-side Runtime Adapter and reported via
    // DeviceHeartbeat.ConfigurationPublishedUtc.
    DateTime PublishedUtc,
    // decision-log.md ADR-066 - checked by DeviceConfigRuntimeAdapter
    // before flattening; a mismatch skips this one device rather than
    // silently binding a shape it doesn't recognize.
    int SchemaVersion,
    // decision-log.md ADR-069 - the real monotonic version number,
    // independent of SchemaVersion (which versions the wire *shape*, not
    // this specific device's *content*). Written on every publish
    // (including the legacy flat blob) even though only the
    // versions/{n}.json blob's own name enforces immutability.
    int ConfigurationVersion,
    string ConfigurationHash);

// Decision-log.md ADR-069 - exactly the subset of DeviceRuntimeConfigWireDocument
// that's actually admin-controlled content; hashed to detect whether a
// publish would produce anything different from what's already published.
internal sealed record DeviceConfigHashableContent(
    DeviceRuntimeConfigWireDeviceSection Device,
    string? OwningAgentId,
    IReadOnlyList<Vivnest.Cloud.Api.Dtos.CapabilityDocumentEntryDto> Capabilities);

internal sealed record DeviceRuntimeConfigWireDeviceSection(
    string Name,
    string? Type,
    bool Enabled,
    string Location,
    string Brand,
    string Model,
    string Firmware,
    IReadOnlyDictionary<string, string> Connection);
