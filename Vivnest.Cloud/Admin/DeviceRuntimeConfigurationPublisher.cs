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
        ILogger<DeviceRuntimeConfigurationPublisher> logger)
    {
        _projector = projector;
        _blobClient = blobClient;
        _deviceEvents = new AzureTableStore<DeviceEventEntity>(tableServiceClient, tablesOptions.Value.DeviceEvents);
        _deviceConfigurations = new AzureTableStore<DeviceConfigurationEntity>(
            tableServiceClient, tablesOptions.Value.DeviceConfiguration);
        _agentCommands = agentCommands;
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

        var publishWarnings = new List<string>();

        var connection = CredentialSettingsFilter.Strip(
            document.Settings, "the device's connection Settings", publishWarnings);

        var capabilities = document.Capabilities
            .Select(c => c with
            {
                Settings = CredentialSettingsFilter.Strip(
                    c.Settings, $"capability \"{c.Name}\"'s Settings", publishWarnings)
            })
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

        var partitionKey = new SiteScope(tenant.TenantId, tenant.SiteId).PartitionKey;

        for (var attempt = 1; attempt <= MaxPublishAttempts; attempt++)
        {
            var existing = await _deviceConfigurations.GetAsync(partitionKey, runtimeDeviceId, cancellationToken);

            if (existing != null && existing.CurrentHash == hash)
            {
                return new DevicePublishResult(
                    false, document, $"Configuration unchanged since version {existing.CurrentVersion}.");
            }

            var newVersion = (existing?.CurrentVersion ?? 0) + 1;
            var publishedUtc = DateTime.UtcNow;

            var wireDocument = new DeviceRuntimeConfigWireDocument(
                runtimeDeviceId, deviceSection, document.OwningAgentId, capabilities,
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

            await WriteAuditEventAsync(tenant, deviceId, runtimeDeviceId, cancellationToken);
            await TryEnqueueRestartAsync(document.OwningAgentId, cancellationToken);

            var resultDocument = publishWarnings.Count > 0
                ? document with { Warnings = publishWarnings }
                : document;

            return new DevicePublishResult(true, resultDocument, null);
        }

        return new DevicePublishResult(false, document, "Concurrent publish detected, please retry.");
    }

    private static string ComputeHash(DeviceConfigHashableContent content)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(content);

        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
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
            await _agentCommands.PublishRestartCommandAsync(runtimeAgentId, cancellationToken);
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
                EventType = DeviceEventTypes.ConfigPublished,
                Severity = "Info",
                OccurredAtUtc = now,
                Payload = JsonSerializer.Serialize(new { runtimeDeviceId })
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
