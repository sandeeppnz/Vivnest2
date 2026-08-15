using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores.Entities;
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
    private readonly IAgentCommandPublisher _agentCommands;
    private readonly ILogger<DeviceRuntimeConfigurationPublisher> _logger;

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

        var wireDocument = new DeviceRuntimeConfigWireDocument(
            runtimeDeviceId,
            new DeviceRuntimeConfigWireDeviceSection(
                document.Name,
                document.Type,
                document.Enabled,
                document.Location,
                document.Brand,
                document.Model,
                document.Firmware,
                connection),
            document.OwningAgentId,
            capabilities,
            DateTime.UtcNow,
            CurrentDeviceSchemaVersion);

        var json = JsonSerializer.SerializeToUtf8Bytes(wireDocument);

        await _blobClient.UploadAsync(
            DeviceConfigBlob.ContainerName,
            DeviceConfigBlob.BlobName(runtimeDeviceId),
            new MemoryStream(json),
            cancellationToken: cancellationToken);

        await WriteAuditEventAsync(tenant, deviceId, runtimeDeviceId, cancellationToken);
        await TryEnqueueRestartAsync(document.OwningAgentId, cancellationToken);

        var resultDocument = publishWarnings.Count > 0
            ? document with { Warnings = publishWarnings }
            : document;

        return new DevicePublishResult(true, resultDocument, null);
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
    int SchemaVersion);

internal sealed record DeviceRuntimeConfigWireDeviceSection(
    string Name,
    string? Type,
    bool Enabled,
    string Location,
    string Brand,
    string Model,
    string Firmware,
    IReadOnlyDictionary<string, string> Connection);
