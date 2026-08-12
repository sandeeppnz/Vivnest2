using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;

namespace Vivnest.Cloud.Admin;

// Maps the persistence-agnostic Device domain model (Vivnest.Core.Domain)
// to/from DeviceRegistryEntity for storage (decision-log.md ADR-057) -
// renamed from DeviceRegistryManagementService, same reasoning
// IDeviceService documents. A genuine hard delete, same reasoning as
// AgentRegistryManagementService - this is a declared identity record
// meant to actually shrink, not an audit trail. DeviceTypeId/OwningAgentId
// existence is the caller's responsibility, same no-FK-validation
// convention as elsewhere. Settings may hold credentials (ADR-050) - see
// DeviceRegistryEntity.Settings's own comment for what that means.
//
// No longer touches CapabilityIds - see DeviceRegistryDto/DeviceRegistryEntity's
// own comments; capability assignment is CapabilityAssignmentService's job now.
public sealed class DeviceService : IDeviceService
{
    private static readonly IReadOnlyDictionary<string, string> EmptySettings =
        new Dictionary<string, string>();

    private readonly IDeviceRegistryStore _deviceRegistry;

    public DeviceService(IDeviceRegistryStore deviceRegistry)
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
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken = default)
    {
        var device = new Device(
            tenant.TenantId,
            tenant.SiteId,
            name,
            deviceTypeId,
            owningAgentId,
            location,
            brand,
            model,
            firmware,
            enabled,
            settings);

        var entity = ToEntity(device);

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
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken = default)
    {
        var entity = await _deviceRegistry.GetAsync(tenant.TenantId, tenant.SiteId, deviceId, cancellationToken);

        if (entity == null)
            return null;

        var device = ToDomain(entity);
        device.Update(name, deviceTypeId, owningAgentId, location, brand, model, firmware, enabled, settings);

        var updated = ToEntity(device);
        updated.ETag = entity.ETag;

        await _deviceRegistry.UpdateAsync(updated, cancellationToken);

        return ToDto(updated);
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

    private static Device ToDomain(DeviceRegistryEntity entity)
    {
        return Device.Rehydrate(
            entity.TenantId,
            entity.SiteId,
            entity.RowKey,
            entity.Name,
            entity.DeviceTypeId,
            entity.OwningAgentId,
            entity.Location,
            entity.Brand,
            entity.Model,
            entity.Firmware,
            entity.Enabled,
            ParseSettings(entity.Settings));
    }

    private static DeviceRegistryEntity ToEntity(Device device)
    {
        return new DeviceRegistryEntity
        {
            PartitionKey = new SiteScope(device.TenantId, device.SiteId).PartitionKey,
            RowKey = device.DeviceId,
            TenantId = device.TenantId,
            SiteId = device.SiteId,
            Name = device.Name,
            DeviceTypeId = device.DeviceTypeId,
            OwningAgentId = device.OwningAgentId,
            Location = device.Location,
            Brand = device.Brand,
            Model = device.Model,
            Firmware = device.Firmware,
            Enabled = device.Enabled,
            Settings = SerializeSettings(device.Settings)
        };
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
            ParseSettings(entity.Settings),
            entity.TenantId,
            entity.SiteId);
    }

    private static string SerializeSettings(IReadOnlyDictionary<string, string>? settings)
    {
        if (settings == null || settings.Count == 0)
            return "{}";

        return System.Text.Json.JsonSerializer.Serialize(settings);
    }

    private static IReadOnlyDictionary<string, string> ParseSettings(string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
            return EmptySettings;

        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(settings)
            ?? new Dictionary<string, string>();
    }
}
