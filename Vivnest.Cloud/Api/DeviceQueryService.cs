using System.Text.Json;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Api;

public sealed class DeviceQueryService : IDeviceQueryService
{
    // Was 15 minutes - raised once genuinely long browser sessions (a tab
    // left open, a slow retry, a captures page scrolled back to later)
    // started outliving the old window mid-view. Doesn't by itself make
    // repeat page loads cache-hit - GenerateReadSasUri still signs a fresh
    // URL on every call regardless of validFor - see ADR-029.
    private static readonly TimeSpan ImageUrlValidFor = TimeSpan.FromHours(24);

    // Captures are immutable once written (a fresh blob per capture, never
    // overwritten) - safe to tell the browser to cache forever. This is a
    // SAS response-header override (see AzureBlobStorageClient.GenerateReadSasUri),
    // so it applies even to blobs uploaded before AzureBlobStorage started
    // setting this at upload time too.
    private const string ImageCacheControl = "public, max-age=31536000, immutable";

    private readonly IDeviceHeartbeatReader _deviceHeartbeats;
    private readonly IDeviceEventReader _deviceEvents;
    private readonly IAgentHeartbeatReader _agentHeartbeats;
    private readonly IBlobStorageService _blobStorage;
    private readonly IDeviceStatusResolver _statusResolver;

    public DeviceQueryService(
        IDeviceHeartbeatReader deviceHeartbeats,
        IDeviceEventReader deviceEvents,
        IAgentHeartbeatReader agentHeartbeats,
        IBlobStorageService blobStorage,
        IDeviceStatusResolver statusResolver)
    {
        _deviceHeartbeats = deviceHeartbeats;
        _deviceEvents = deviceEvents;
        _agentHeartbeats = agentHeartbeats;
        _blobStorage = blobStorage;
        _statusResolver = statusResolver;
    }

    public async Task<IReadOnlyList<DeviceSummaryDto>> GetDevicesAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default)
    {
        var entities = await _deviceHeartbeats.GetByTenantAsync(
            tenant.TenantId,
            tenant.SiteId,
            cancellationToken);

        var agents = await _agentHeartbeats.GetByTenantAsync(
            tenant.TenantId,
            tenant.SiteId,
            cancellationToken);

        var agentsByAgentId = agents.ToDictionary(a => a.AgentId);

        // Same PartitionKey ("{TenantId}|{SiteId}|{AgentId}") for a device
        // and its parent - they're always on the same agent.
        var entitiesByKey = entities.ToDictionary(e => (e.PartitionKey, e.RowKey));

        return entities
            .Select(e => ToDto(
                e,
                agentsByAgentId.GetValueOrDefault(e.AgentId),
                GetParentOrDefault(e, entitiesByKey)))
            .ToList();
    }

    private static DeviceHeartbeatEntity? GetParentOrDefault(
        DeviceHeartbeatEntity entity,
        IReadOnlyDictionary<(string PartitionKey, string RowKey), DeviceHeartbeatEntity> entitiesByKey)
    {
        if (string.IsNullOrWhiteSpace(entity.ParentDeviceId))
            return null;

        return entitiesByKey.GetValueOrDefault((entity.PartitionKey, entity.ParentDeviceId));
    }

    public async Task<DeviceSummaryDto?> GetDeviceAsync(
        TenantContext tenant,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _deviceHeartbeats.GetByTenantAsync(
            tenant.TenantId,
            tenant.SiteId,
            cancellationToken);

        var entity = entities.FirstOrDefault(e =>
            string.Equals(e.RowKey, deviceId, StringComparison.Ordinal));

        if (entity is null)
            return null;

        var agent = await _agentHeartbeats.GetAsync(
            $"{entity.TenantId}|{entity.SiteId}",
            entity.AgentId,
            cancellationToken);

        DeviceHeartbeatEntity? parentDevice = null;

        if (!string.IsNullOrWhiteSpace(entity.ParentDeviceId))
        {
            parentDevice = await _deviceHeartbeats.GetAsync(
                entity.PartitionKey,
                entity.ParentDeviceId,
                cancellationToken);
        }

        return ToDto(entity, agent, parentDevice);
    }

    public async Task<IReadOnlyList<DeviceEventDto>> GetDeviceEventsAsync(
        TenantContext tenant,
        string deviceId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var entities = await _deviceEvents.GetByDeviceAsync(
            tenant.TenantId,
            tenant.SiteId,
            deviceId,
            eventType: null,
            take,
            cancellationToken);

        return entities.Select(e => ToDto(e, includeImageUrl: false)).ToList();
    }

    public async Task<IReadOnlyList<DeviceEventDto>> GetDeviceCapturesAsync(
        TenantContext tenant,
        string deviceId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var entities = await _deviceEvents.GetByDeviceAsync(
            tenant.TenantId,
            tenant.SiteId,
            deviceId,
            eventType: DeviceEventTypes.CameraCaptured,
            take,
            cancellationToken);

        return entities.Select(e => ToDto(e, includeImageUrl: true)).ToList();
    }

    public async Task<IReadOnlyList<DeviceEventDto>> GetDeviceBatteryReadingsAsync(
        TenantContext tenant,
        string deviceId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var entities = await _deviceEvents.GetByDeviceAsync(
            tenant.TenantId,
            tenant.SiteId,
            deviceId,
            eventType: DeviceEventTypes.BatteryStatus,
            take,
            cancellationToken);

        return entities.Select(e => ToDto(e, includeImageUrl: false)).ToList();
    }

    public async Task<IReadOnlyList<CaptureDaySummaryDto>> GetDeviceCaptureDaySummariesAsync(
        TenantContext tenant,
        string deviceId,
        int days,
        CancellationToken cancellationToken = default)
    {
        var toUtc = DateTime.UtcNow;
        var fromUtc = toUtc.Date.AddDays(-(days - 1));

        var entities = await _deviceEvents.GetByDeviceAndDateRangeAsync(
            tenant.TenantId,
            tenant.SiteId,
            deviceId,
            eventType: DeviceEventTypes.CameraCaptured,
            fromUtc,
            toUtc,
            cancellationToken);

        // Deliberately skip ToDto here - no JSON payload parsing, no SAS
        // URL generation, since none of that is needed just to count.
        return entities
            .GroupBy(e => DateOnly.FromDateTime(e.OccurredAtUtc))
            .Select(g => new CaptureDaySummaryDto(g.Key, g.Count()))
            .OrderByDescending(s => s.Date)
            .ToList();
    }

    public async Task<CapturePageDto> GetDeviceCapturesByDayAsync(
        TenantContext tenant,
        string deviceId,
        DateOnly date,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var fromUtc = date.ToDateTime(TimeOnly.MinValue);
        var toUtc = fromUtc.AddDays(1);

        // Bounded to one day, not the whole window - GetByDeviceAndDateRangeAsync
        // already returns newest-first.
        var entities = await _deviceEvents.GetByDeviceAndDateRangeAsync(
            tenant.TenantId,
            tenant.SiteId,
            deviceId,
            eventType: DeviceEventTypes.CameraCaptured,
            fromUtc,
            toUtc,
            cancellationToken);

        var page = entities.Skip(skip).Take(take).ToList();
        var hasMore = skip + page.Count < entities.Count;

        // SAS URLs (the expensive part) only get generated for this page,
        // not the rest of the day's captures.
        var dtos = page.Select(e => ToDto(e, includeImageUrl: true)).ToList();

        return new CapturePageDto(dtos, hasMore);
    }

    private DeviceSummaryDto ToDto(
        DeviceHeartbeatEntity entity,
        AgentHeartbeatEntity? agent,
        DeviceHeartbeatEntity? parentDevice)
    {
        var result = _statusResolver.Determine(entity, agent, parentDevice);

        return new DeviceSummaryDto(
            DeviceId: entity.RowKey,
            DeviceType: entity.DeviceType,
            Status: result.Status.ToString(),
            StatusSinceUtc: result.StatusSinceUtc,
            LastHeartbeatUtc: entity.LastHeartbeatUtc,
            LastActivityUtc: entity.LastActivityUtc,
            HeartbeatInterval: TableTimeSpan.Parse(entity.ExpectedLivenessInterval),
            AgentId: entity.AgentId,
            TenantId: entity.TenantId,
            SiteId: entity.SiteId,
            Error: entity.Error,
            ParentDeviceId: string.IsNullOrWhiteSpace(entity.ParentDeviceId) ? null : entity.ParentDeviceId);
    }

    private DeviceEventDto ToDto(DeviceEventEntity entity, bool includeImageUrl)
    {
        JsonElement? data = null;

        try
        {
            data = JsonSerializer.Deserialize<JsonElement>(entity.Payload);
        }
        catch (JsonException)
        {
            // Leave Data null if the payload isn't valid JSON.
        }

        var imageUrl = includeImageUrl
            ? TryGenerateImageUrl(data)
            : null;

        return new DeviceEventDto(
            EventType: entity.EventType,
            Severity: entity.Severity,
            OccurredAtUtc: entity.OccurredAtUtc,
            Data: data,
            ImageUrl: imageUrl);
    }

    private string? TryGenerateImageUrl(JsonElement? data)
    {
        if (data is not { } json)
            return null;

        if (!json.TryGetProperty("BlobContainer", out var containerProp)
            || !json.TryGetProperty("BlobName", out var blobNameProp))
            return null;

        var container = containerProp.GetString();
        var blobName = blobNameProp.GetString();

        if (string.IsNullOrEmpty(container) || string.IsNullOrEmpty(blobName))
            return null;

        return _blobStorage
            .GenerateReadSasUri(container, blobName, ImageUrlValidFor, ImageCacheControl)
            .ToString();
    }
}
