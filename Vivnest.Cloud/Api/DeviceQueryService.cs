using System.Text.Json;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Constants;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Api;

public sealed class DeviceQueryService : IDeviceQueryService
{
    private static readonly TimeSpan ImageUrlValidFor = TimeSpan.FromMinutes(15);

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

        return entities
            .Select(e => ToDto(e, agentsByAgentId.GetValueOrDefault(e.AgentId)))
            .ToList();
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

        return ToDto(entity, agent);
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

    public async Task<IReadOnlyList<DeviceEventDto>> GetDeviceCapturesByDateRangeAsync(
        TenantContext tenant,
        string deviceId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var entities = await _deviceEvents.GetByDeviceAndDateRangeAsync(
            tenant.TenantId,
            tenant.SiteId,
            deviceId,
            eventType: DeviceEventTypes.CameraCaptured,
            fromUtc,
            toUtc,
            cancellationToken);

        return entities.Select(e => ToDto(e, includeImageUrl: true)).ToList();
    }

    private DeviceSummaryDto ToDto(DeviceHeartbeatEntity entity, AgentHeartbeatEntity? agent)
    {
        return new DeviceSummaryDto(
            DeviceId: entity.RowKey,
            DeviceType: entity.DeviceType,
            Status: _statusResolver.Determine(entity, agent).Status.ToString(),
            LastHeartbeatUtc: entity.LastHeartbeatUtc,
            LastActivityUtc: entity.LastActivityUtc,
            Error: entity.Error);
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
            .GenerateReadSasUri(container, blobName, ImageUrlValidFor)
            .ToString();
    }
}
