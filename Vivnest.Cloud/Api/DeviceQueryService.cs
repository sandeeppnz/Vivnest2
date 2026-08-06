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

        // Fanned out in parallel rather than awaited one at a time in the
        // Select below - each is an independent Table query (only cameras
        // incur one at all), negligible at current device counts.
        var thumbnailUrls = await Task.WhenAll(
            entities.Select(e => TryGetThumbnailUrlAsync(tenant, e, cancellationToken)));

        return entities
            .Select((e, i) => ToDto(
                e,
                agentsByAgentId.GetValueOrDefault(e.AgentId),
                GetParentOrDefault(e, entitiesByKey),
                thumbnailUrls[i]))
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

        var thumbnailUrl = await TryGetThumbnailUrlAsync(tenant, entity, cancellationToken);

        return ToDto(entity, agent, parentDevice, thumbnailUrl);
    }

    // Deliberately not sourced from DeviceHeartbeat's own denormalized
    // fields (Timezone/Brand/Model/Firmware's pattern) - that path only
    // publishes on a status change (ADR-005), so a blob name stamped there
    // would freeze at whatever was captured the moment status last flipped,
    // not the latest capture. Queried fresh per device instead, reusing the
    // same event lookup GetDeviceCapturesAsync already does with take: 1.
    private async Task<string?> TryGetThumbnailUrlAsync(
        TenantContext tenant,
        DeviceHeartbeatEntity entity,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(entity.DeviceType, "Camera", StringComparison.Ordinal))
            return null;

        var events = await _deviceEvents.GetByDeviceAsync(
            tenant.TenantId,
            tenant.SiteId,
            entity.RowKey,
            eventType: DeviceEventTypes.CameraCaptured,
            take: 1,
            cancellationToken);

        var latest = events.FirstOrDefault();
        if (latest is null)
            return null;

        JsonElement? data;

        try
        {
            data = JsonSerializer.Deserialize<JsonElement>(latest.Payload);
        }
        catch (JsonException)
        {
            return null;
        }

        return TryGenerateImageUrl(data);
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
        var tz = await ResolveDeviceTimezoneAsync(tenant, deviceId, cancellationToken);

        var toUtc = DateTime.UtcNow;

        // "Last N calendar days" in the device's own timezone, not UTC -
        // otherwise a device 12+ hours ahead of UTC (e.g. NZT) has its late-
        // evening captures grouped a day early. See ADR for the full
        // reasoning (Cloud has no concept of "the viewer's timezone" - this
        // is the site's timezone, stamped by the Agent at heartbeat time).
        var localToday = TimeZoneInfo.ConvertTimeFromUtc(toUtc, tz).Date;
        var fromLocalMidnight = DateTime.SpecifyKind(localToday.AddDays(-(days - 1)), DateTimeKind.Unspecified);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(fromLocalMidnight, tz);

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
            .GroupBy(e => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(e.OccurredAtUtc, tz)))
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
        var tz = await ResolveDeviceTimezoneAsync(tenant, deviceId, cancellationToken);

        // date is a local calendar date (the device's timezone) - convert
        // its local midnight-to-midnight span to UTC bounds, rather than
        // treating the date as if it were already a UTC date.
        var localMidnight = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(localMidnight, tz);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(localMidnight.AddDays(1), tz);

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

    // Same full-tenant-scan-then-filter shape GetDeviceAsync already uses -
    // there's no direct partitionKey-free lookup by deviceId alone. Falls
    // back to UTC on any resolution failure (device not found, no timezone
    // configured, or an invalid/unrecognized IANA id) rather than throwing -
    // day-grouping degrading to UTC is a much smaller problem than the
    // whole gallery erroring out.
    private async Task<TimeZoneInfo> ResolveDeviceTimezoneAsync(
        TenantContext tenant,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var entities = await _deviceHeartbeats.GetByTenantAsync(
            tenant.TenantId,
            tenant.SiteId,
            cancellationToken);

        var entity = entities.FirstOrDefault(e =>
            string.Equals(e.RowKey, deviceId, StringComparison.Ordinal));

        return ResolveTimezone(entity?.Timezone);
    }

    private static TimeZoneInfo ResolveTimezone(string? timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private DeviceSummaryDto ToDto(
        DeviceHeartbeatEntity entity,
        AgentHeartbeatEntity? agent,
        DeviceHeartbeatEntity? parentDevice,
        string? thumbnailUrl)
    {
        var result = _statusResolver.Determine(entity, agent, parentDevice);

        return new DeviceSummaryDto(
            DeviceId: entity.RowKey,
            Name: entity.Name ?? string.Empty,
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
            ParentDeviceId: string.IsNullOrWhiteSpace(entity.ParentDeviceId) ? null : entity.ParentDeviceId,
            Timezone: string.IsNullOrWhiteSpace(entity.Timezone) ? "UTC" : entity.Timezone,
            Location: entity.Location ?? string.Empty,
            Brand: entity.Brand ?? string.Empty,
            Model: entity.Model ?? string.Empty,
            Firmware: entity.Firmware ?? string.Empty,
            ThumbnailUrl: thumbnailUrl);
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
