using System.Text.Json;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Admin;

// A genuine hard delete, same reasoning as AgentRegistryManagementService -
// this is a declared identity record meant to actually shrink, not an
// audit trail. DeviceTypeId/OwningAgentId existence is the caller's
// responsibility, same no-FK-validation convention as elsewhere. Settings
// may hold credentials (ADR-050) - see DeviceRegistryEntity.Settings's own
// comment for what that means.
public sealed class DeviceRegistryManagementService : IDeviceRegistryManagementService
{
    private static readonly IReadOnlyDictionary<string, string> EmptySettings =
        new Dictionary<string, string>();

    private readonly IDeviceRegistryStore _deviceRegistry;

    public DeviceRegistryManagementService(IDeviceRegistryStore deviceRegistry)
    {
        _deviceRegistry = deviceRegistry;
    }

    public async Task<IReadOnlyList<DeviceRegistryDto>> ListAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default)
    {
        var entities = await _deviceRegistry.ListAsync(tenant.TenantId, tenant.SiteId, cancellationToken);

        return entities.Select(ToDto).ToList();
    }

    public async Task<DeviceRegistryDto> CreateAsync(
        TenantContext tenant,
        string name,
        string deviceTypeId,
        string owningAgentId,
        string location,
        string brand,
        string model,
        string firmware,
        bool enabled,
        IReadOnlyList<Guid>? capabilityIds,
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken = default)
    {
        var entity = new DeviceRegistryEntity
        {
            PartitionKey = $"{tenant.TenantId}|{tenant.SiteId}",
            RowKey = Guid.NewGuid().ToString(),
            TenantId = tenant.TenantId,
            SiteId = tenant.SiteId,
            Name = name,
            DeviceTypeId = deviceTypeId,
            OwningAgentId = owningAgentId,
            Location = location,
            Brand = brand,
            Model = model,
            Firmware = firmware,
            Enabled = enabled,
            CapabilityIds = SerializeCapabilityIds(capabilityIds),
            Settings = SerializeSettings(settings)
        };

        await _deviceRegistry.CreateAsync(entity, cancellationToken);

        return ToDto(entity);
    }

    public async Task<DeviceRegistryDto?> UpdateAsync(
        TenantContext tenant,
        string deviceId,
        string name,
        string deviceTypeId,
        string owningAgentId,
        string location,
        string brand,
        string model,
        string firmware,
        bool enabled,
        IReadOnlyList<Guid>? capabilityIds,
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken = default)
    {
        var entity = await _deviceRegistry.GetAsync(tenant.TenantId, tenant.SiteId, deviceId, cancellationToken);

        if (entity == null)
            return null;

        entity.Name = name;
        entity.DeviceTypeId = deviceTypeId;
        entity.OwningAgentId = owningAgentId;
        entity.Location = location;
        entity.Brand = brand;
        entity.Model = model;
        entity.Firmware = firmware;
        entity.Enabled = enabled;
        entity.CapabilityIds = SerializeCapabilityIds(capabilityIds);
        entity.Settings = SerializeSettings(settings);

        await _deviceRegistry.UpdateAsync(entity, cancellationToken);

        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(
        TenantContext tenant,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _deviceRegistry.GetAsync(tenant.TenantId, tenant.SiteId, deviceId, cancellationToken);

        if (entity == null)
            return false;

        await _deviceRegistry.DeleteAsync(tenant.TenantId, tenant.SiteId, deviceId, cancellationToken);

        return true;
    }

    private static DeviceRegistryDto ToDto(DeviceRegistryEntity entity)
    {
        return new DeviceRegistryDto(
            Guid.Parse(entity.RowKey),
            entity.Name,
            entity.DeviceTypeId,
            entity.OwningAgentId,
            entity.Location,
            entity.Brand,
            entity.Model,
            entity.Firmware,
            entity.Enabled,
            ParseCapabilityIds(entity.CapabilityIds),
            ParseSettings(entity.Settings),
            entity.TenantId,
            entity.SiteId);
    }

    private static string SerializeCapabilityIds(IReadOnlyList<Guid>? capabilityIds)
    {
        if (capabilityIds == null || capabilityIds.Count == 0)
            return "";

        return string.Join(',', capabilityIds);
    }

    private static IReadOnlyList<Guid> ParseCapabilityIds(string capabilityIds)
    {
        if (string.IsNullOrWhiteSpace(capabilityIds))
            return [];

        return capabilityIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(Guid.Parse)
            .ToList();
    }

    private static string SerializeSettings(IReadOnlyDictionary<string, string>? settings)
    {
        if (settings == null || settings.Count == 0)
            return "{}";

        return JsonSerializer.Serialize(settings);
    }

    private static IReadOnlyDictionary<string, string> ParseSettings(string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
            return EmptySettings;

        return JsonSerializer.Deserialize<Dictionary<string, string>>(settings) ?? new Dictionary<string, string>();
    }
}
