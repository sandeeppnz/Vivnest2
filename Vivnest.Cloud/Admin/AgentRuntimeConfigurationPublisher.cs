using Azure;
using System.Text.Json;
using System.Text.Json.Nodes;
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



public sealed class AgentRuntimeConfigurationPublisher : IAgentRuntimeConfigurationPublisher
{
    // Matches the real blob's real key name exactly (confirmed against the
    // live 3a56ad98-...json blob) - System.Text.Json.Nodes key lookups are
    // case-sensitive, so a mismatched case here would leave a stale
    // "AiClassification" key alongside a new, differently-cased one
    // instead of replacing it.
    private const string AiClassificationKey = "AiClassification";
    private const string CapabilitiesKey = "Capabilities";

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

    // The Agent side of the shared pipeline - see RuntimeConfigurationWriter.
    // See DeviceRuntimeConfigurationPublisher.Target - same shape, and the
    // name funcs take a ConfigBlobKey so the writer can derive both the
    // scoped and legacy names from them.
    private static readonly ConfigurationPublishTarget<AgentConfigurationEntity> Target = new(
        "Agent",
        AgentConfigBlob.ContainerName,
        AgentConfigBlob.VersionBlobName,
        AgentConfigBlob.ManifestBlobName,
        AgentConfigBlob.BlobName,
        row => new AgentConfigurationEntity
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

    private readonly IAgentRuntimeConfigurationProjector _projector;
    private readonly IBlobStorageClient _blobClient;
    private readonly IAgentEventStore _agentEvents;
    private readonly RuntimeConfigurationWriter<AgentConfigurationEntity> _writer;

    public AgentRuntimeConfigurationPublisher(
        IAgentRuntimeConfigurationProjector projector,
        IBlobStorageClient blobClient,
        IAgentEventStore agentEvents,
        RuntimeConfigurationWriter<AgentConfigurationEntity> writer)
    {
        _projector = projector;
        _blobClient = blobClient;
        _agentEvents = agentEvents;
        _writer = writer;
    }

    public async Task<AgentPublishResult?> PublishAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var document =
            await _projector.ProjectAsync(
                tenant,
                agentId,
                cancellationToken);

        if (document == null)
            return null;

        if (document.Warnings.Count > 0)
        {
            return new AgentPublishResult(
                false,
                document,
                $"Cannot publish: {string.Join(" ", document.Warnings)}");
        }

        // Warnings empty guarantees this - AgentRuntimeConfigurationProjector
        // only ever returns a null AgentId alongside a Warnings entry.
        var runtimeAgentId = document.AgentId!;

        if (!_writer.TryGetEncryptionKey(
                out var encryptionKey,
                out var keyError))
        {
            return new AgentPublishResult(
                false,
                document,
                keyError);
        }

        // ------------------------------------------------------------
        // Build capability wire entries
        //
        // These are the capabilities that the Cloud projection has
        // determined are actively assigned to this Agent.
        // ------------------------------------------------------------

        var capabilities =
            document.Capabilities
                .Select(x =>
                    new AgentCapabilityWireEntry(
                        x.CapabilityId,
                        x.CapabilityKey,
                        x.Name,
                        x.Enabled,
                        x.Settings))
                .ToList();

        // ------------------------------------------------------------
        // Build hashable content
        //
        // Capabilities MUST participate in the hash.
        //
        // Otherwise a change such as:
        //
        //     Agent A
        //       + camera.capture
        //
        // becoming:
        //
        //     Agent A
        //       + camera.capture
        //       + motion.detect
        //
        // would not produce a new configuration hash.
        // ------------------------------------------------------------

        var hash =
            RuntimeConfigurationWriter<AgentConfigurationEntity>
                .ComputeHash(
                    new AgentConfigHashableContent(
                        new AiClassificationWireSection(
                            document.Devices),
                        capabilities,
                        document.Name));

        // ------------------------------------------------------------
        // Encrypt credential-shaped AI configuration fields.
        //
        // Existing behaviour - unchanged.
        // ------------------------------------------------------------

        var devices =
            document.Devices
                .Select(d =>
                    new AiDeviceClassificationEntryDto(
                        d.DeviceId,

                        d.ObjectDetection == null
                            ? null
                            : CredentialCipher.EncryptFields(
                                d.ObjectDetection,
                                encryptionKey),

                        d.SinkCleanliness == null
                            ? null
                            : CredentialCipher.EncryptFields(
                                d.SinkCleanliness,
                                encryptionKey)))
                .ToList();

        var aiClassification =
            new AiClassificationWireSection(devices);

        // ------------------------------------------------------------
        // Publish version
        // ------------------------------------------------------------

        var result =
            await WriteVersionAsync(
                tenant,
                runtimeAgentId,
                aiClassification,
                capabilities,
                hash,
                document.Name,
                bypassNoOpCheck: false,
                cancellationToken);

        if (!result.Success)
        {
            return new AgentPublishResult(
                false,
                document,
                result.Reason);
        }

        // ------------------------------------------------------------
        // Audit
        // ------------------------------------------------------------

        await WriteAuditEventAsync(
            tenant,
            agentId,
            runtimeAgentId,
            AgentEventTypes.ConfigPublished,
            new
            {
                runtimeAgentId
            },
            cancellationToken);

        // ------------------------------------------------------------
        // Restart Agent so the new configuration is loaded.
        // ------------------------------------------------------------

        await _writer.TryEnqueueRestartAsync(
            tenant,
            runtimeAgentId,
            "ConfigPublish",
            cancellationToken);

        return new AgentPublishResult(
            true,
            document,
            null);
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

        // Scoped first, then legacy - see the matching comment in
        // DeviceRuntimeConfigurationPublisher.RollbackAsync.
        var targetBytes = await TryDownloadVersionAsync(
            new ConfigBlobKey(tenant.TenantId, tenant.SiteId, runtimeAgentId), targetVersion, cancellationToken);

        if (targetBytes == null)
            return new AgentPublishResult(false, document, $"Version {targetVersion} does not exist for this agent.");

        targetDocument = JsonSerializer.Deserialize<AgentConfigWireDocument>(targetBytes)
            ?? throw new JsonException("Version blob deserialized to null.");

        var result = await WriteVersionAsync(
            tenant,
            runtimeAgentId,
            targetDocument.AiClassification,
            targetDocument.Capabilities,
            targetDocument.ConfigurationHash,
            document.Name,
            bypassNoOpCheck: true,
            cancellationToken);

        if (!result.Success)
            return new AgentPublishResult(false, document, result.Reason);

        await WriteAuditEventAsync(
            tenant, agentId, runtimeAgentId, AgentEventTypes.ConfigRolledBack,
            new { runtimeAgentId, rolledBackFromVersion = targetVersion, newVersion = result.Version },
            cancellationToken);
        await _writer.TryEnqueueRestartAsync(tenant, runtimeAgentId, "ConfigRollback", cancellationToken);

        var resultDocument = document with
        {
            Devices = targetDocument.AiClassification.Devices,

            Capabilities = targetDocument.Capabilities
                .Select(x =>
                    new AgentCapabilityRuntimeDto(
                        x.CapabilityId,
                        x.CapabilityKey,
                        x.Name,
                        x.Enabled,
                        x.Settings))
                .ToList(),

            Warnings = Array.Empty<string>(),
        };

        return new AgentPublishResult(true, resultDocument, null);
    }

    private async Task<byte[]?> TryDownloadVersionAsync(
        ConfigBlobKey key, int version, CancellationToken cancellationToken)
    {
        try
        {
            return await _blobClient.DownloadAsync(
                AgentConfigBlob.ContainerName,
                AgentConfigBlob.VersionBlobName(key, version),
                cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    // Everything Agent-specific about a write: the versioned document's own
    // shape, and the fact that the flat blob is a merge/patch onto whatever
    // is already there rather than a straight copy of it. The retry cycle
    // around both lives in RuntimeConfigurationWriter.
    private Task<VersionWriteResult> WriteVersionAsync(
        TenantContext tenant,
        string runtimeAgentId,
        AiClassificationWireSection aiClassification,
        IReadOnlyList<AgentCapabilityWireEntry> capabilities,

        string hash,
        string? name,
        bool bypassNoOpCheck,
        CancellationToken cancellationToken) =>
        _writer.WriteVersionAsync(
            tenant,
            runtimeAgentId,
            Target,
            hash,
            bypassNoOpCheck,
            (newVersion, publishedUtc) => JsonSerializer.SerializeToUtf8Bytes(
              new AgentConfigWireDocument(
                    runtimeAgentId,
                    aiClassification,
                    capabilities,
                    publishedUtc,
                    CurrentAgentSchemaVersion,
                    newVersion,
                    hash,
                    name)),
            async (newVersion, publishedUtc, _) =>
            {
                // Merge/patch onto whatever's already on the flat blob
                // (preserves e.g. a Low-type agent's own HomeAssistant
                // section), unchanged from ADR-064 onward. "Run alongside,"
                // never replaced - which is why this side ignores the
                // versioned bytes that the Device side simply reuses here.
                var root = await LoadExistingBlobAsync(
                    new ConfigBlobKey(tenant.TenantId, tenant.SiteId, runtimeAgentId), cancellationToken);

                root[AiClassificationKey] = JsonSerializer.SerializeToNode(aiClassification);
                root[CapabilitiesKey] = JsonSerializer.SerializeToNode(capabilities);
                root[ConfigurationPublishedUtcKey] = JsonValue.Create(publishedUtc);
                root[ConfigurationSchemaVersionKey] = JsonValue.Create(CurrentAgentSchemaVersion);
                root[ConfigurationVersionKey] = JsonValue.Create(newVersion);
                root[ConfigurationHashKey] = JsonValue.Create(hash);
                root[NameKey] = JsonValue.Create(name);

                return JsonSerializer.SerializeToUtf8Bytes(root);
            },
            cancellationToken);

    // The merge base for the flat blob - the agent-local sections this
    // merge exists to preserve (a Low-type agent's HomeAssistant) live
    // there and nowhere else.
    private async Task<JsonObject> LoadExistingBlobAsync(
        ConfigBlobKey key, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _blobClient.DownloadAsync(
                AgentConfigBlob.ContainerName, AgentConfigBlob.BlobName(key), cancellationToken);

            return JsonNode.Parse(existing) as JsonObject ?? new JsonObject();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // No blob for this agent - a brand-new Agent has nothing to
            // preserve, same tolerance the Device publisher has.
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

internal sealed record AgentConfigWireDocument(
    string RuntimeAgentId,
    AiClassificationWireSection AiClassification,
    IReadOnlyList<AgentCapabilityWireEntry> Capabilities,
    DateTime PublishedUtc,
    int SchemaVersion,
    int ConfigurationVersion,
    string ConfigurationHash,
    string? Name = null);

internal sealed record AgentCapabilityWireEntry(
    string CapabilityId,
    string CapabilityKey,
    string Name,
    bool Enabled,
    IReadOnlyDictionary<string, string> Settings);

internal sealed record AgentConfigHashableContent(
    AiClassificationWireSection AiClassification,
    IReadOnlyList<AgentCapabilityWireEntry> Capabilities,
    string? Name);