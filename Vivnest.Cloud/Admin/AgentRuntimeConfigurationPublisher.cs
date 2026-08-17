using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Options;
using Vivnest.Core.Security;
using Vivnest.Core.Storage;
using static Vivnest.Core.Constants.RuntimeConfigurationSchemaVersions;
using Vivnest.Core.DataStores;

namespace Vivnest.Cloud.Admin;

public sealed class AgentRuntimeConfigurationPublisher : IAgentRuntimeConfigurationPublisher
{
    // Matches the real blob's real key name exactly (confirmed against the
    // live 3a56ad98-...json blob) - System.Text.Json.Nodes key lookups are
    // case-sensitive, so a mismatched case here would leave a stale
    // "AiClassification" key alongside a new, differently-cased one
    // instead of replacing it.
    private const string AiClassificationKey = "AiClassification";

    // Sibling top-level key to AiClassification (Phase 6C / decision-log.md
    // ADR-065) - Admin's only other owned key on this blob. Bound at
    // configuration root by AgentConfigMetadataOptions, reported via
    // AgentHeartbeat.ConfigurationPublishedUtc.
    private const string ConfigurationPublishedUtcKey = "ConfigurationPublishedUtc";

    // decision-log.md ADR-066 - another sibling top-level key, checked by
    // PlatformAgentHeartbeatWorker against RuntimeConfigurationSchemaVersions.CurrentAgentSchemaVersion.
    private const string ConfigurationSchemaVersionKey = "ConfigurationSchemaVersion";

    // decision-log.md ADR-069 - two more sibling top-level keys, additive
    // to the legacy flat blob so even an Agent build that only reads this
    // path can report them.
    private const string ConfigurationVersionKey = "ConfigurationVersion";
    private const string ConfigurationHashKey = "ConfigurationHash";

    // decision-log.md ADR-087 - another sibling top-level key: the Admin
    // registry's own Name (AgentRegistryEntity.Name), replacing the old
    // design where each Agent process self-reported whatever Name its own
    // local appsettings.json happened to have.
    private const string NameKey = "Name";

    private readonly IAgentRuntimeConfigurationProjector _projector;
    private readonly AzureBlobStorageClient _blobClient;
    private readonly AzureTableStore<AgentEventEntity> _agentEvents;
    private readonly AzureTableStore<AgentConfigurationEntity> _agentConfigurations;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IOptions<CredentialEncryptionOptions> _credentialEncryption;
    private readonly ILogger<AgentRuntimeConfigurationPublisher> _logger;

    // See DeviceRuntimeConfigurationPublisher.MaxPublishAttempts (decision-log.md
    // ADR-069) - same reasoning, mirrored here.
    private const int MaxPublishAttempts = 3;

    public AgentRuntimeConfigurationPublisher(
        IAgentRuntimeConfigurationProjector projector,
        AzureBlobStorageClient blobClient,
        TableServiceClient tableServiceClient,
        IOptions<TablesOptions> tablesOptions,
        ICommandDispatcher commandDispatcher,
        IOptions<CredentialEncryptionOptions> credentialEncryption,
        ILogger<AgentRuntimeConfigurationPublisher> logger)
    {
        _projector = projector;
        _blobClient = blobClient;
        _agentEvents = new AzureTableStore<AgentEventEntity>(tableServiceClient, tablesOptions.Value.AgentEvents);
        _agentConfigurations = new AzureTableStore<AgentConfigurationEntity>(
            tableServiceClient, tablesOptions.Value.AgentConfiguration);
        _commandDispatcher = commandDispatcher;
        _credentialEncryption = credentialEncryption;
        _logger = logger;
    }

    public async Task<AgentPublishResult?> PublishAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _projector.ProjectAsync(tenant, agentId, cancellationToken);

        if (document == null)
            return null;

        if (document.Warnings.Count > 0)
        {
            return new AgentPublishResult(
                false, document, $"Cannot publish: {string.Join(" ", document.Warnings)}");
        }

        // Warnings empty guarantees this - AgentRuntimeConfigurationProjector
        // only ever returns a null AgentId alongside a Warnings entry.
        var runtimeAgentId = document.AgentId!;

        if (!TryGetEncryptionKey(out var encryptionKey, out var keyError))
            return new AgentPublishResult(false, document, keyError);

        // decision-log.md ADR-085 - credential-shaped keys are still
        // published, but as ciphertext under the shared CredentialEncryption
        // key rather than plaintext (ADR-084) or stripped out entirely
        // (ADR-064/038).
        var devices = document.Devices
            .Select(d => new AiDeviceClassificationEntryDto(
                d.DeviceId,
                d.ObjectDetection == null ? null : CredentialCipher.EncryptFields(d.ObjectDetection, encryptionKey),
                d.SinkCleanliness == null ? null : CredentialCipher.EncryptFields(d.SinkCleanliness, encryptionKey)))
            .ToList();

        // Hashed/versioned content is AiClassification plus Name - both
        // things Admin actually controls on this blob (decision-log.md
        // ADR-069/ADR-087). Agent-local sections (e.g. a Low-type agent's
        // HomeAssistant) are never part of "desired state" at all, so they
        // must never affect whether a republish is considered a real
        // change. Name has to be included here, not just written
        // alongside the hash - otherwise a Name-only change (AiClassification
        // unchanged) would hit the no-op guard below and never actually
        // publish, since that guard compares against this exact hash.
        var aiClassification = new AiClassificationWireSection(devices);
        var hash = ComputeHash(new AgentConfigHashableContent(aiClassification, document.Name));

        var result = await WriteVersionAsync(
            tenant, runtimeAgentId, aiClassification, hash, document.Name, bypassNoOpCheck: false, cancellationToken);

        if (!result.Success)
            return new AgentPublishResult(false, document, result.Reason);

        await WriteAuditEventAsync(
            tenant, agentId, runtimeAgentId, AgentEventTypes.ConfigPublished,
            new { runtimeAgentId }, cancellationToken);
        await TryEnqueueRestartAsync(tenant, runtimeAgentId, "ConfigPublish", cancellationToken);

        return new AgentPublishResult(true, document, null);
    }

    // Decision-log.md ADR-070 - see DeviceRuntimeConfigurationPublisher.RollbackAsync
    // for the full reasoning (identical here): republishes an old
    // immutable version's AiClassification content verbatim as a new
    // version, never mutating targetVersion's own blob, without
    // re-projecting from live Admin state.
    public async Task<AgentPublishResult?> RollbackAsync(
        TenantContext tenant,
        string agentId,
        int targetVersion,
        CancellationToken cancellationToken = default)
    {
        var document = await _projector.ProjectAsync(tenant, agentId, cancellationToken);

        if (document == null)
            return null;

        if (document.Warnings.Count > 0)
        {
            return new AgentPublishResult(
                false, document, $"Cannot roll back: {string.Join(" ", document.Warnings)}");
        }

        var runtimeAgentId = document.AgentId!;

        AgentConfigWireDocument targetDocument;

        try
        {
            var targetBytes = await _blobClient.DownloadAsync(
                AgentConfigBlob.ContainerName,
                AgentConfigBlob.VersionBlobName(runtimeAgentId, targetVersion),
                cancellationToken);

            targetDocument = JsonSerializer.Deserialize<AgentConfigWireDocument>(targetBytes)
                ?? throw new JsonException("Version blob deserialized to null.");
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new AgentPublishResult(false, document, $"Version {targetVersion} does not exist for this agent.");
        }

        var result = await WriteVersionAsync(
            tenant, runtimeAgentId, targetDocument.AiClassification, targetDocument.ConfigurationHash,
            document.Name, bypassNoOpCheck: true, cancellationToken);

        if (!result.Success)
            return new AgentPublishResult(false, document, result.Reason);

        await WriteAuditEventAsync(
            tenant, agentId, runtimeAgentId, AgentEventTypes.ConfigRolledBack,
            new { runtimeAgentId, rolledBackFromVersion = targetVersion, newVersion = result.Version },
            cancellationToken);
        await TryEnqueueRestartAsync(tenant, runtimeAgentId, "ConfigRollback", cancellationToken);

        var resultDocument = document with
        {
            Devices = targetDocument.AiClassification.Devices,
            Warnings = Array.Empty<string>(),
        };

        return new AgentPublishResult(true, resultDocument, null);
    }

    // Decision-log.md ADR-070 - the write-version-blob -> overwrite-manifest
    // -> overwrite-legacy-flat-blob (merge-patch) -> update-metadata-row
    // retry cycle, shared by PublishAsync and RollbackAsync - see
    // DeviceRuntimeConfigurationPublisher.WriteVersionAsync for the full
    // reasoning, mirrored here.
    private async Task<VersionWriteResult> WriteVersionAsync(
        TenantContext tenant,
        string runtimeAgentId,
        AiClassificationWireSection aiClassification,
        string hash,
        string? name,
        bool bypassNoOpCheck,
        CancellationToken cancellationToken)
    {
        var partitionKey = new SiteScope(tenant.TenantId, tenant.SiteId).PartitionKey;

        for (var attempt = 1; attempt <= MaxPublishAttempts; attempt++)
        {
            var existing = await _agentConfigurations.GetAsync(partitionKey, runtimeAgentId, cancellationToken);

            if (!bypassNoOpCheck && existing != null && existing.CurrentHash == hash)
            {
                return new VersionWriteResult(
                    false, existing.CurrentVersion, $"Configuration unchanged since version {existing.CurrentVersion}.");
            }

            var newVersion = (existing?.CurrentVersion ?? 0) + 1;
            var publishedUtc = DateTime.UtcNow;

            var versionedDocument = new AgentConfigWireDocument(
                runtimeAgentId, aiClassification, publishedUtc, CurrentAgentSchemaVersion, newVersion, hash, name);

            var versionedJson = JsonSerializer.SerializeToUtf8Bytes(versionedDocument);

            try
            {
                await _blobClient.UploadAsync(
                    AgentConfigBlob.ContainerName,
                    AgentConfigBlob.VersionBlobName(runtimeAgentId, newVersion),
                    new MemoryStream(versionedJson),
                    failIfExists: true,
                    cancellationToken: cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                _logger.LogWarning(
                    "Agent {RuntimeAgentId} version {Version} was claimed by a concurrent publish; retrying (attempt {Attempt}/{Max}).",
                    runtimeAgentId, newVersion, attempt, MaxPublishAttempts);

                continue;
            }

            var manifest = new ConfigurationManifest(
                newVersion, hash, AgentConfigBlob.VersionBlobName(runtimeAgentId, newVersion), publishedUtc);

            await _blobClient.UploadAsync(
                AgentConfigBlob.ContainerName,
                AgentConfigBlob.ManifestBlobName(runtimeAgentId),
                new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(manifest)),
                cancellationToken: cancellationToken);

            // Legacy flat blob - a merge/patch onto whatever's already
            // there (preserves e.g. a Low-type agent's own HomeAssistant
            // section), unchanged from ADR-064 onward. "Run alongside,"
            // never replaced.
            var root = await LoadExistingBlobAsync(runtimeAgentId, cancellationToken);

            root[AiClassificationKey] = JsonSerializer.SerializeToNode(aiClassification);
            root[ConfigurationPublishedUtcKey] = JsonValue.Create(publishedUtc);
            root[ConfigurationSchemaVersionKey] = JsonValue.Create(CurrentAgentSchemaVersion);
            root[ConfigurationVersionKey] = JsonValue.Create(newVersion);
            root[ConfigurationHashKey] = JsonValue.Create(hash);
            root[NameKey] = JsonValue.Create(name);

            await _blobClient.UploadAsync(
                AgentConfigBlob.ContainerName,
                AgentConfigBlob.BlobName(runtimeAgentId),
                new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(root)),
                cancellationToken: cancellationToken);

            var entity = new AgentConfigurationEntity
            {
                PartitionKey = partitionKey,
                RowKey = runtimeAgentId,
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
                    await _agentConfigurations.UpsertAsync(entity, cancellationToken);
                else
                    await _agentConfigurations.UpdateAsync(entity, cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                // See DeviceRuntimeConfigurationPublisher's own copy of this
                // catch block - same reasoning, same tolerable cost.
                _logger.LogWarning(
                    "Agent {RuntimeAgentId} configuration metadata was updated concurrently; retrying (attempt {Attempt}/{Max}).",
                    runtimeAgentId, attempt, MaxPublishAttempts);

                continue;
            }

            return new VersionWriteResult(true, newVersion, null);
        }

        return new VersionWriteResult(false, -1, "Concurrent publish detected, please retry.");
    }

    private static string ComputeHash(AgentConfigHashableContent content)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(content);

        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    // See DeviceRuntimeConfigurationPublisher.TryGetEncryptionKey
    // (decision-log.md ADR-085) - same reasoning, mirrored here.
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

    // Decision-log.md ADR-068 - see DeviceRuntimeConfigurationPublisher's
    // own copy of this method for the full reasoning. Best-effort, never
    // fails a publish that already succeeded.
    private async Task TryEnqueueRestartAsync(
        TenantContext tenant,
        string runtimeAgentId,
        string requestedBy,
        CancellationToken cancellationToken)
    {
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

    private async Task<JsonObject> LoadExistingBlobAsync(
        string runtimeAgentId, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _blobClient.DownloadAsync(
                AgentConfigBlob.ContainerName, AgentConfigBlob.BlobName(runtimeAgentId), cancellationToken);

            return JsonNode.Parse(existing) as JsonObject ?? new JsonObject();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // No blob for this agent yet - a brand-new Agent has nothing
            // to preserve, same tolerance the Device publisher has.
            return new JsonObject();
        }
    }

    private async Task WriteAuditEventAsync(
        TenantContext tenant,
        string agentId,
        string runtimeAgentId,
        string eventType,
        object payload,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        await _agentEvents.UpsertAsync(
            new AgentEventEntity
            {
                PartitionKey = agentId,
                RowKey = EventRowKey.New(now),
                TenantId = tenant.TenantId,
                SiteId = tenant.SiteId,
                AgentId = agentId,
                EventType = eventType,
                Severity = "Info",
                OccurredAtUtc = now,
                Payload = JsonSerializer.Serialize(payload)
            },
            cancellationToken);
    }
}

// Matches the real AiClassificationOptions shape (Vivnest.Core.Options)
// exactly, PascalCase, so it slots into the existing IConfiguration-bound
// blob unchanged from Vivnest.Agent's perspective (decision-log.md ADR-064).
internal sealed record AiClassificationWireSection(IReadOnlyList<AiDeviceClassificationEntryDto> Devices);

// decision-log.md ADR-087 - hashed together so a Name-only change (no
// AiClassification change) still bumps the hash and clears the no-op
// guard in WriteVersionAsync. Not itself written to any blob - purely
// the hash-input shape, distinct from AgentConfigWireDocument below.
internal sealed record AgentConfigHashableContent(AiClassificationWireSection AiClassification, string? Name);

// The new agent-config/{runtimeAgentId}/versions/{n}.json shape
// (decision-log.md ADR-069) - deliberately self-contained, representing
// only Admin's own AiClassification contribution, not a merge with
// whatever Agent-local sections (e.g. HomeAssistant) happen to exist on
// the legacy flat blob - those were never part of "desired state."
internal sealed record AgentConfigWireDocument(
    string RuntimeAgentId,
    AiClassificationWireSection AiClassification,
    DateTime PublishedUtc,
    int SchemaVersion,
    int ConfigurationVersion,
    string ConfigurationHash,
    // decision-log.md ADR-087 - the Admin registry's own Name, written
    // fresh from live Admin state on every publish/rollback (never itself
    // versioned/hashed content, same treatment as PublishedUtc above).
    string? Name = null);
