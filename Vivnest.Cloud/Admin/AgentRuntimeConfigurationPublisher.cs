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

    private readonly IAgentRuntimeConfigurationProjector _projector;
    private readonly AzureBlobStorageClient _blobClient;
    private readonly AzureTableStore<AgentEventEntity> _agentEvents;
    private readonly IAgentCommandPublisher _agentCommands;
    private readonly ILogger<AgentRuntimeConfigurationPublisher> _logger;

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

        var root = await LoadExistingBlobAsync(runtimeAgentId, cancellationToken);

        root[AiClassificationKey] = JsonSerializer.SerializeToNode(new AiClassificationWireSection(devices));
        root[ConfigurationPublishedUtcKey] = JsonValue.Create(DateTime.UtcNow);
        root[ConfigurationSchemaVersionKey] = JsonValue.Create(CurrentAgentSchemaVersion);

        var json = JsonSerializer.SerializeToUtf8Bytes(root);

        await _blobClient.UploadAsync(
            AgentConfigBlob.ContainerName,
            AgentConfigBlob.BlobName(runtimeAgentId),
            new MemoryStream(json),
            cancellationToken: cancellationToken);

        await WriteAuditEventAsync(tenant, agentId, runtimeAgentId, cancellationToken);
        await TryEnqueueRestartAsync(runtimeAgentId, cancellationToken);

        var resultDocument = publishWarnings.Count > 0
            ? document with { Warnings = publishWarnings }
            : document;

        return new AgentPublishResult(true, resultDocument, null);
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
