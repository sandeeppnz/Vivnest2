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
using Vivnest.Core.Storage;
using static Vivnest.Core.Constants.RuntimeConfigurationSchemaVersions;

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
    // AgentHeartbeatWorker against RuntimeConfigurationSchemaVersions.CurrentAgentSchemaVersion.
    private const string ConfigurationSchemaVersionKey = "ConfigurationSchemaVersion";

    // decision-log.md ADR-069 - two more sibling top-level keys, additive
    // to the legacy flat blob so even an Agent build that only reads this
    // path can report them.
    private const string ConfigurationVersionKey = "ConfigurationVersion";
    private const string ConfigurationHashKey = "ConfigurationHash";

    private readonly IAgentRuntimeConfigurationProjector _projector;
    private readonly AzureBlobStorageClient _blobClient;
    private readonly AzureTableStore<AgentEventEntity> _agentEvents;
    private readonly AzureTableStore<AgentConfigurationEntity> _agentConfigurations;
    private readonly IAgentCommandPublisher _agentCommands;
    private readonly ILogger<AgentRuntimeConfigurationPublisher> _logger;

    // See DeviceRuntimeConfigurationPublisher.MaxPublishAttempts (decision-log.md
    // ADR-069) - same reasoning, mirrored here.
    private const int MaxPublishAttempts = 3;

    public AgentRuntimeConfigurationPublisher(
        IAgentRuntimeConfigurationProjector projector,
        AzureBlobStorageClient blobClient,
        TableServiceClient tableServiceClient,
        IOptions<TablesOptions> tablesOptions,
        IAgentCommandPublisher agentCommands,
        ILogger<AgentRuntimeConfigurationPublisher> logger)
    {
        _projector = projector;
        _blobClient = blobClient;
        _agentEvents = new AzureTableStore<AgentEventEntity>(tableServiceClient, tablesOptions.Value.AgentEvents);
        _agentConfigurations = new AzureTableStore<AgentConfigurationEntity>(
            tableServiceClient, tablesOptions.Value.AgentConfiguration);
        _agentCommands = agentCommands;
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

        var publishWarnings = new List<string>();

        var devices = document.Devices
            .Select(d => new AiDeviceClassificationEntryDto(
                d.DeviceId,
                d.ObjectDetection == null
                    ? null
                    : CredentialSettingsFilter.Strip(
                        d.ObjectDetection, $"device \"{d.DeviceId}\"'s ObjectDetection settings", publishWarnings),
                d.SinkCleanliness == null
                    ? null
                    : CredentialSettingsFilter.Strip(
                        d.SinkCleanliness, $"device \"{d.DeviceId}\"'s SinkCleanliness settings", publishWarnings)))
            .ToList();

        // Hashed/versioned content is just the AiClassification section -
        // the only thing Admin actually controls on this blob (decision-log.md
        // ADR-069). Agent-local sections (e.g. a Low-type agent's
        // HomeAssistant) are never part of "desired state" at all, so they
        // must never affect whether a republish is considered a real
        // change.
        var aiClassification = new AiClassificationWireSection(devices);
        var hash = ComputeHash(aiClassification);

        var partitionKey = new SiteScope(tenant.TenantId, tenant.SiteId).PartitionKey;

        for (var attempt = 1; attempt <= MaxPublishAttempts; attempt++)
        {
            var existing = await _agentConfigurations.GetAsync(partitionKey, runtimeAgentId, cancellationToken);

            if (existing != null && existing.CurrentHash == hash)
            {
                return new AgentPublishResult(
                    false, document, $"Configuration unchanged since version {existing.CurrentVersion}.");
            }

            var newVersion = (existing?.CurrentVersion ?? 0) + 1;
            var publishedUtc = DateTime.UtcNow;

            var versionedDocument = new AgentConfigWireDocument(
                runtimeAgentId, aiClassification, publishedUtc, CurrentAgentSchemaVersion, newVersion, hash);

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

            await WriteAuditEventAsync(tenant, agentId, runtimeAgentId, cancellationToken);
            await TryEnqueueRestartAsync(runtimeAgentId, cancellationToken);

            var resultDocument = publishWarnings.Count > 0
                ? document with { Warnings = publishWarnings }
                : document;

            return new AgentPublishResult(true, resultDocument, null);
        }

        return new AgentPublishResult(false, document, "Concurrent publish detected, please retry.");
    }

    private static string ComputeHash(AiClassificationWireSection content)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(content);

        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    // Decision-log.md ADR-068 - see DeviceRuntimeConfigurationPublisher's
    // own copy of this method for the full reasoning. Best-effort, never
    // fails a publish that already succeeded.
    private async Task TryEnqueueRestartAsync(string runtimeAgentId, CancellationToken cancellationToken)
    {
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
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        await _agentEvents.UpsertAsync(
            new AgentEventEntity
            {
                PartitionKey = agentId,
                RowKey = $"{now:yyyyMMddHHmmssfff}-{Guid.NewGuid()}",
                TenantId = tenant.TenantId,
                SiteId = tenant.SiteId,
                AgentId = agentId,
                EventType = AgentEventTypes.ConfigPublished,
                Severity = "Info",
                OccurredAtUtc = now,
                Payload = JsonSerializer.Serialize(new { runtimeAgentId })
            },
            cancellationToken);
    }
}

// Matches the real AiClassificationOptions shape (Vivnest.Core.Options)
// exactly, PascalCase, so it slots into the existing IConfiguration-bound
// blob unchanged from Vivnest.Agent's perspective (decision-log.md ADR-064).
internal sealed record AiClassificationWireSection(IReadOnlyList<AiDeviceClassificationEntryDto> Devices);

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
    string ConfigurationHash);
