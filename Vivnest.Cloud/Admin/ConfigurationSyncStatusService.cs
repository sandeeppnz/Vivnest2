using System.Text.Json;
using Azure;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Admin;

// Decision-log.md ADR-068. "Desired" is never re-fetched here - it's
// whichever already-projected document the caller passes in, since
// projection is cheap and the Function handler already did it. Only
// PublishedUtc (the currently published blob's own value) and AppliedUtc/
// ApplyError (the latest heartbeat's) need fetching, both best-effort - a
// missing blob or heartbeat is a real, reportable state (NeverPublished/
// Unknown), not an error.
//
// RuntimeDeviceId/RuntimeAgentId identify the heartbeat row directly
// (DeviceHeartbeatEntity.PartitionKey = "{TenantId}|{SiteId}|{RuntimeAgentId}",
// RowKey = RuntimeDeviceId; AgentHeartbeatEntity.PartitionKey =
// "{TenantId}|{SiteId}", RowKey = RuntimeAgentId - both writers stamp the
// resolved runtime identity, not the admin one) - already sitting on the
// projected document (DeviceEntry.DeviceId/OwningAgentId,
// AgentEntry.AgentId are the resolved RuntimeDeviceId/RuntimeAgentId by
// the time projection succeeds), so no extra registry lookup is needed to
// resolve identity here.
public sealed class ConfigurationSyncStatusService : IConfigurationSyncStatusService
{
    // Read-only consumer (two DownloadAsync calls, nothing else), so it
    // takes the read-side IBlobStorageService rather than the raw
    // AzureBlobStorageClient. The two config publishers in this same
    // folder legitimately still take the raw client - IBlobStorageService
    // deliberately has no UploadAsync, so the read/write split is a real
    // capability boundary, not an inconsistency to flatten.
    private readonly IBlobStorageService _blobClient;
    private readonly IDeviceHeartbeatReader _deviceHeartbeats;
    private readonly IAgentHeartbeatReader _agentHeartbeats;

    public ConfigurationSyncStatusService(
        IBlobStorageService blobClient,
        IDeviceHeartbeatReader deviceHeartbeats,
        IAgentHeartbeatReader agentHeartbeats)
    {
        _blobClient = blobClient;
        _deviceHeartbeats = deviceHeartbeats;
        _agentHeartbeats = agentHeartbeats;
    }

    public async Task<ConfigurationSyncStatusDto?> GetDeviceStatusAsync(
        TenantContext tenant,
        DeviceRuntimeConfigurationDocumentDto document,
        CancellationToken cancellationToken = default)
    {
        if (document.Warnings.Count > 0
            || string.IsNullOrWhiteSpace(document.DeviceId)
            || string.IsNullOrWhiteSpace(document.OwningAgentId))
        {
            return null;
        }

        var runtimeDeviceId = document.DeviceId;
        var runtimeAgentId = document.OwningAgentId;

        // Decision-log.md ADR-069 - manifest first (cheap, and the
        // authoritative source of PublishedVersion/PublishedHash); a
        // missing manifest (never republished through the new pipeline)
        // falls back to the legacy flat blob's own PublishedUtc peek,
        // exactly as ADR-068 originally built - neither path is a special
        // case of the other.
        var (publishedUtc, publishedVersion, publishedHash) = await TryReadManifestAsync(
            DeviceConfigBlob.ContainerName, DeviceConfigBlob.ManifestBlobName,
            new ConfigBlobKey(tenant.TenantId, tenant.SiteId, runtimeDeviceId), cancellationToken);

        if (publishedUtc == null)
        {
            publishedUtc = await TryReadPublishedUtcAsync<DeviceBlobHeader>(
                DeviceConfigBlob.ContainerName,
                DeviceConfigBlob.BlobName,
                new ConfigBlobKey(tenant.TenantId, tenant.SiteId, runtimeDeviceId),
                static header => header.PublishedUtc,
                cancellationToken);
        }

        if (publishedUtc == null)
        {
            return new ConfigurationSyncStatusDto(
                null, null, null, ConfigurationSyncStatus.NeverPublished, null, null, null);
        }

        var deviceHeartbeatPartitionKey = $"{new SiteScope(tenant.TenantId, tenant.SiteId).PartitionKey}|{runtimeAgentId}";
        var deviceHeartbeat = await _deviceHeartbeats.GetAsync(
            deviceHeartbeatPartitionKey, runtimeDeviceId, cancellationToken);

        // Coarse (decision-log.md ADR-068, confirmed with the user) - the
        // Agent only ever reports a load error on its own heartbeat, not
        // per-device, so a device's Status can't distinguish "this
        // specific device's config was invalid" from "some device this
        // agent owns had an invalid config." Still more useful than
        // silence: an admin investigating a stuck device is pointed at
        // the right agent's error message.
        var agentHeartbeatPartitionKey = new SiteScope(tenant.TenantId, tenant.SiteId).PartitionKey;
        var agentHeartbeat = await _agentHeartbeats.GetAsync(
            agentHeartbeatPartitionKey, runtimeAgentId, cancellationToken);

        return BuildStatus(
            publishedUtc, deviceHeartbeat?.ConfigurationPublishedUtc,
            publishedVersion, deviceHeartbeat?.ConfigurationVersion,
            publishedHash, deviceHeartbeat?.ConfigurationHash,
            agentHeartbeat?.ConfigurationLoadError);
    }

    public async Task<ConfigurationSyncStatusDto?> GetAgentStatusAsync(
        TenantContext tenant,
        AgentRuntimeConfigurationDocumentDto document,
        CancellationToken cancellationToken = default)
    {
        if (document.Warnings.Count > 0 || string.IsNullOrWhiteSpace(document.AgentId))
            return null;

        var runtimeAgentId = document.AgentId;

        var (publishedUtc, publishedVersion, publishedHash) = await TryReadManifestAsync(
            AgentConfigBlob.ContainerName, AgentConfigBlob.ManifestBlobName,
            new ConfigBlobKey(tenant.TenantId, tenant.SiteId, runtimeAgentId), cancellationToken);

        if (publishedUtc == null)
        {
            publishedUtc = await TryReadPublishedUtcAsync<AgentBlobHeader>(
                AgentConfigBlob.ContainerName,
                AgentConfigBlob.BlobName,
                new ConfigBlobKey(tenant.TenantId, tenant.SiteId, runtimeAgentId),
                static header => header.ConfigurationPublishedUtc,
                cancellationToken);
        }

        if (publishedUtc == null)
        {
            return new ConfigurationSyncStatusDto(
                null, null, null, ConfigurationSyncStatus.NeverPublished, null, null, null);
        }

        var partitionKey = new SiteScope(tenant.TenantId, tenant.SiteId).PartitionKey;
        var agentHeartbeat = await _agentHeartbeats.GetAsync(partitionKey, runtimeAgentId, cancellationToken);

        return BuildStatus(
            publishedUtc, agentHeartbeat?.ConfigurationPublishedUtc,
            publishedVersion, agentHeartbeat?.ConfigurationVersion,
            publishedHash, agentHeartbeat?.ConfigurationHash,
            agentHeartbeat?.ConfigurationLoadError);
    }

    public async Task<ConfigurationSyncStatusDto> GetDeviceStatusFromHeartbeatAsync(
        TenantContext tenant,
        DeviceHeartbeatEntity device,
        string? applyError,
        CancellationToken cancellationToken = default)
    {
        var runtimeDeviceId = device.RowKey;

        var (publishedUtc, publishedVersion, publishedHash) = await TryReadManifestAsync(
            DeviceConfigBlob.ContainerName, DeviceConfigBlob.ManifestBlobName,
            new ConfigBlobKey(tenant.TenantId, tenant.SiteId, runtimeDeviceId), cancellationToken);

        if (publishedUtc == null)
        {
            publishedUtc = await TryReadPublishedUtcAsync<DeviceBlobHeader>(
                DeviceConfigBlob.ContainerName,
                DeviceConfigBlob.BlobName,
                new ConfigBlobKey(tenant.TenantId, tenant.SiteId, runtimeDeviceId),
                static header => header.PublishedUtc,
                cancellationToken);
        }

        if (publishedUtc == null)
        {
            return new ConfigurationSyncStatusDto(
                null, null, null, ConfigurationSyncStatus.NeverPublished, null, null, null);
        }

        return BuildStatus(
            publishedUtc, device.ConfigurationPublishedUtc,
            publishedVersion, device.ConfigurationVersion,
            publishedHash, device.ConfigurationHash,
            applyError);
    }

    public async Task<ConfigurationSyncStatusDto> GetAgentStatusFromHeartbeatAsync(
        TenantContext tenant,
        AgentHeartbeatEntity agent,
        CancellationToken cancellationToken = default)
    {
        var runtimeAgentId = agent.RowKey;

        var (publishedUtc, publishedVersion, publishedHash) = await TryReadManifestAsync(
            AgentConfigBlob.ContainerName, AgentConfigBlob.ManifestBlobName,
            new ConfigBlobKey(tenant.TenantId, tenant.SiteId, runtimeAgentId), cancellationToken);

        if (publishedUtc == null)
        {
            publishedUtc = await TryReadPublishedUtcAsync<AgentBlobHeader>(
                AgentConfigBlob.ContainerName,
                AgentConfigBlob.BlobName,
                new ConfigBlobKey(tenant.TenantId, tenant.SiteId, runtimeAgentId),
                static header => header.ConfigurationPublishedUtc,
                cancellationToken);
        }

        if (publishedUtc == null)
        {
            return new ConfigurationSyncStatusDto(
                null, null, null, ConfigurationSyncStatus.NeverPublished, null, null, null);
        }

        return BuildStatus(
            publishedUtc, agent.ConfigurationPublishedUtc,
            publishedVersion, agent.ConfigurationVersion,
            publishedHash, agent.ConfigurationHash,
            agent.ConfigurationLoadError);
    }

    private static ConfigurationSyncStatusDto BuildStatus(
        DateTime? publishedUtc, DateTime? appliedUtc,
        int? publishedVersion, int? appliedVersion,
        string? publishedHash, string? appliedHash,
        string? applyError)
    {
        // Decision-log.md ADR-069 - version/hash compared directly when
        // both sides have them (more precise than a timestamp - an exact
        // content match, not just "some publish happened after some
        // apply"); falls back to the timestamp comparison ADR-068
        // originally built when either side is still on the legacy path.
        // Neither comparison is a special case of the other - both are
        // real, valid ways to reach the same conclusion.
        bool? upToDate = publishedVersion != null && appliedVersion != null
            ? publishedVersion == appliedVersion && publishedHash == appliedHash
            : appliedUtc == null
                ? null
                : appliedUtc == publishedUtc;

        var status = !string.IsNullOrWhiteSpace(applyError)
            ? ConfigurationSyncStatus.Failed
            : upToDate == null
                ? ConfigurationSyncStatus.Unknown
                : upToDate.Value
                    ? ConfigurationSyncStatus.UpToDate
                    : ConfigurationSyncStatus.Pending;

        return new ConfigurationSyncStatusDto(
            publishedUtc, appliedUtc, applyError, status, publishedVersion, appliedVersion, publishedHash);
    }

    // Scoped layout first, legacy second. During the transition both
    // exist and agree; after it, only the scoped one does; before any
    // republish, only the legacy one does. Trying in that order means this
    // service reports the same thing throughout, with no flag to set.
    private async Task<(DateTime? PublishedUtc, int? Version, string? Hash)> TryReadManifestAsync(
        string containerName, Func<ConfigBlobKey, string> manifestBlobName, ConfigBlobKey key,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in new[] { key, key.Unscoped() })
        {
            var found = await ReadManifestAsync(
                containerName, manifestBlobName(candidate), cancellationToken);

            if (found.PublishedUtc != null)
                return found;
        }

        return (null, null, null);
    }

    private async Task<DateTime?> TryReadPublishedUtcAsync<THeader>(
        string containerName,
        Func<ConfigBlobKey, string> blobName,
        ConfigBlobKey key,
        Func<THeader, DateTime?> selectPublishedUtc,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in new[] { key, key.Unscoped() })
        {
            var found = await ReadPublishedUtcAsync(
                containerName, blobName(candidate), selectPublishedUtc, cancellationToken);

            if (found != null)
                return found;
        }

        return null;
    }

    private async Task<(DateTime? PublishedUtc, int? Version, string? Hash)> ReadManifestAsync(
        string containerName, string manifestBlobName, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _blobClient.DownloadAsync(containerName, manifestBlobName, cancellationToken);
            var manifest = JsonSerializer.Deserialize<ConfigurationManifest>(bytes);

            return manifest == null
                ? (null, null, null)
                : (manifest.PublishedUtc, manifest.ConfigurationVersion, manifest.ConfigurationHash);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return (null, null, null);
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private async Task<DateTime?> ReadPublishedUtcAsync<THeader>(
        string containerName,
        string blobName,
        Func<THeader, DateTime?> selectPublishedUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _blobClient.DownloadAsync(containerName, blobName, cancellationToken);
            var header = JsonSerializer.Deserialize<THeader>(bytes);

            return header == null ? null : selectPublishedUtc(header);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// Minimal read shapes - System.Text.Json ignores unmapped members by
// default, so these deliberately don't mirror the full wire-document
// shape (DeviceRuntimeConfigWireDocument/the Agent blob's full root),
// which live internal to each publisher.
file sealed record DeviceBlobHeader(DateTime? PublishedUtc);

file sealed record AgentBlobHeader(DateTime? ConfigurationPublishedUtc);
