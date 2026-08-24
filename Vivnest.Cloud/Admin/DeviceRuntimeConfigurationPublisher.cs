using System.Text.Json;
using Azure;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Security;
using Vivnest.Core.Storage;
using static Vivnest.Core.Constants.RuntimeConfigurationSchemaVersions;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Admin;

public sealed class DeviceRuntimeConfigurationPublisher : IDeviceRuntimeConfigurationPublisher
{
    // No naming policy - every real config file in this codebase
    // (common-config.json, appsettings.json, hand-authored device-config/
    // agent-config blobs) uses PascalCase field names matching the C#
    // Options classes exactly, so the wire record types below are
    // deliberately declared with PascalCase properties and serialized
    // with System.Text.Json's default (as-declared) naming.

    // The Device side of the shared pipeline - see RuntimeConfigurationWriter.
    // The name funcs take a ConfigBlobKey, so the writer builds the scoped
    // and legacy names from the same three delegates rather than needing a
    // second set for the transition.
    private static readonly ConfigurationPublishTarget<DeviceConfigurationEntity> Target = new(
        "Device",
        DeviceConfigBlob.ContainerName,
        DeviceConfigBlob.VersionBlobName,
        DeviceConfigBlob.ManifestBlobName,
        DeviceConfigBlob.BlobName,
        row => new DeviceConfigurationEntity
        {
            PartitionKey = row.PartitionKey,
            RowKey = row.RowKey,
            TenantId = row.TenantId,
            SiteId = row.SiteId,
            CurrentVersion = row.Version,
            CurrentHash = row.Hash,
            PublishedUtc = row.PublishedUtc,
            ETag = row.ETag
        });

    private readonly IDeviceRuntimeConfigurationProjector _projector;
    private readonly IBlobStorageClient _blobClient;
    private readonly IDeviceEventStore _deviceEvents;
    private readonly RuntimeConfigurationWriter<DeviceConfigurationEntity> _writer;

    public DeviceRuntimeConfigurationPublisher(
        IDeviceRuntimeConfigurationProjector projector,
        IBlobStorageClient blobClient,
        IDeviceEventStore deviceEvents,
        RuntimeConfigurationWriter<DeviceConfigurationEntity> writer)
    {
        _projector = projector;
        _blobClient = blobClient;
        _deviceEvents = deviceEvents;
        _writer = writer;
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

        if (!_writer.TryGetEncryptionKey(out var encryptionKey, out var keyError))
            return new DevicePublishResult(false, document, keyError);

        // Hash the PLAINTEXT, then encrypt. The order matters and used to
        // be the other way around: CredentialCipher.Encrypt draws a fresh
        // random AES-GCM nonce per call, so hashing the ciphertext made
        // byte-identical admin data hash differently every single time,
        // and the ADR-069 no-op guard never fired for any device carrying
        // a credential-shaped key - i.e. every real camera. The hash
        // answers "did the admin change anything"; ciphertext is not admin
        // data.
        var plaintextSection = BuildDeviceSection(document, document.Settings);

        var hash = RuntimeConfigurationWriter<DeviceConfigurationEntity>.ComputeHash(
            new DeviceConfigHashableContent(
                plaintextSection, document.OwningAgentId, document.Capabilities),
            encryptionKey);

        // decision-log.md ADR-085 - credential-shaped keys (RtspPassword etc.)
        // are still published, but as ciphertext under the shared
        // CredentialEncryption key rather than plaintext (ADR-084) or
        // stripped out entirely (ADR-064/038).
        var deviceSection = BuildDeviceSection(
            document, CredentialCipher.EncryptFields(document.Settings, encryptionKey));

        var capabilities = document.Capabilities
            .Select(c => c with { Settings = CredentialCipher.EncryptFields(c.Settings, encryptionKey) })
            .ToList();

        var result = await WriteVersionAsync(
            tenant, runtimeDeviceId, deviceSection, document.OwningAgentId, capabilities, hash,
            bypassNoOpCheck: false, cancellationToken);

        if (!result.Success)
            return new DevicePublishResult(false, document, result.Reason);

        await WriteAuditEventAsync(
            tenant, deviceId, runtimeDeviceId, DeviceEventTypes.ConfigPublished,
            new { runtimeDeviceId }, cancellationToken);
        await _writer.TryEnqueueRestartAsync(tenant, document.OwningAgentId, "ConfigPublish", cancellationToken);

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

        // Scoped first, then the legacy layout: a version published before
        // tenant/site scoping only exists at the unscoped name, and it must
        // still be rollable-to.
        var targetBytes = await TryDownloadVersionAsync(
            new ConfigBlobKey(tenant.TenantId, tenant.SiteId, runtimeDeviceId), targetVersion, cancellationToken);

        if (targetBytes == null)
            return new DevicePublishResult(false, document, $"Version {targetVersion} does not exist for this device.");

        targetDocument = JsonSerializer.Deserialize<DeviceRuntimeConfigWireDocument>(targetBytes)
            ?? throw new JsonException("Version blob deserialized to null.");

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
        await _writer.TryEnqueueRestartAsync(tenant, targetDocument.OwningAgentId, "ConfigRollback", cancellationToken);

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

    private async Task<byte[]?> TryDownloadVersionAsync(
        ConfigBlobKey key, int version, CancellationToken cancellationToken)
    {
        try
        {
            return await _blobClient.DownloadAsync(
                DeviceConfigBlob.ContainerName,
                DeviceConfigBlob.VersionBlobName(key, version),
                cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    // Built twice per publish - once over the plaintext Settings to hash,
    // once over the encrypted ones to actually write.
    private static DeviceRuntimeConfigWireDeviceSection BuildDeviceSection(
        DeviceRuntimeConfigurationDocumentDto document,
        IReadOnlyDictionary<string, string> connection) =>
        new(document.Name,
            document.Type,
            document.Enabled,
            document.Location,
            document.Brand,
            document.Model,
            document.Firmware,
            connection,
            document.LivenessIntervalSeconds,
            document.WarningMultiplier);

    // Everything Device-specific about a write: the versioned document's
    // own shape, and the fact that the legacy flat blob gets exactly the
    // same bytes (unlike the Agent side, which merge-patches). The retry
    // cycle around both lives in RuntimeConfigurationWriter.
    private Task<VersionWriteResult> WriteVersionAsync(
        TenantContext tenant,
        string runtimeDeviceId,
        DeviceRuntimeConfigWireDeviceSection deviceSection,
        string? owningAgentId,
        IReadOnlyList<CapabilityDocumentEntryDto> capabilities,
        string hash,
        bool bypassNoOpCheck,
        CancellationToken cancellationToken) =>
        _writer.WriteVersionAsync(
            tenant,
            runtimeDeviceId,
            Target,
            hash,
            bypassNoOpCheck,
            (newVersion, publishedUtc) => JsonSerializer.SerializeToUtf8Bytes(
                new DeviceRuntimeConfigWireDocument(
                    runtimeDeviceId, deviceSection, owningAgentId, capabilities,
                    publishedUtc, CurrentDeviceSchemaVersion, newVersion, hash)),
            (_, _, versionJson) => Task.FromResult(versionJson),
            cancellationToken);

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
                RowKey = EventRowKey.New(now),
                TenantId = tenant.TenantId,
                SiteId = tenant.SiteId,
                AgentId = "",
                DeviceId = deviceId,
                DeviceType = "",
                EventType = eventType,
                Severity = "Information",
                OccurredAtUtc = now,
                Payload = JsonSerializer.Serialize(payload)
            },
            cancellationToken);
    }
}

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
    IReadOnlyList<CapabilityDocumentEntryDto> Capabilities,
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
    IReadOnlyList<CapabilityDocumentEntryDto> Capabilities);

internal sealed record DeviceRuntimeConfigWireDeviceSection(
    string Name,
    string? Type,
    bool Enabled,
    string Location,
    string Brand,
    string Model,
    string Firmware,
    IReadOnlyDictionary<string, string> Connection,
    // ADR-099 - device-owned, never written by a capability adapter.
    int LivenessIntervalSeconds,
    double WarningMultiplier);
