using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Domain;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Admin;

// Maps the persistence-agnostic Device domain model (Vivnest.Core.Domain)
// to/from DeviceRegistryEntity for storage (decision-log.md ADR-057) -
// renamed from DeviceRegistryManagementService, same reasoning
// IDeviceService documents. DeviceTypeId existence is the caller's
// responsibility, same no-FK-validation convention as elsewhere - but
// OwningAgentId IS validated (ADR-058), against IAgentRegistryStore, to
// resolve to a real Agent in the same Tenant/Site. Settings may hold
// credentials (ADR-050) - see DeviceRegistryEntity.Settings's own comment
// for what that means.
//
// No longer touches CapabilityIds - see DeviceRegistryDto/DeviceRegistryEntity's
// own comments; capability assignment is CapabilityAssignmentService's job now.
//
// No hard delete (ADR-058) - a Device's identity must remain stable, so
// retiring one is UpdateAsync(status: Retired), not a DELETE - same
// reasoning MachineManagementService already established.
public sealed class DeviceService : IDeviceService
{
    private static readonly IReadOnlyDictionary<string, string> EmptySettings =
        new Dictionary<string, string>();

    private readonly IDeviceRegistryStore _deviceRegistry;
    private readonly IAgentRegistryStore _agentRegistry;

    public DeviceService(IDeviceRegistryStore deviceRegistry, IAgentRegistryStore agentRegistry)
    {
        _deviceRegistry = deviceRegistry;
        _agentRegistry = agentRegistry;
    }

    public async Task<IReadOnlyList<DeviceRegistryDto>> ListAsync(
        TenantContext tenant,
        string? ownerAgentId = null,
        string? deviceTypeId = null,
        CancellationToken cancellationToken = default)
    {
        var entities = await _deviceRegistry.ListAsync(tenant.TenantId, tenant.SiteId, cancellationToken);

        IEnumerable<DeviceRegistryEntity> filtered = entities;

        if (!string.IsNullOrWhiteSpace(ownerAgentId))
            filtered = filtered.Where(e => e.OwningAgentId == ownerAgentId);

        if (!string.IsNullOrWhiteSpace(deviceTypeId))
            filtered = filtered.Where(e => e.DeviceTypeId == deviceTypeId);

        return filtered.Select(ToDto).ToList();
    }

    public async Task<DeviceRegistryDto?> CreateAsync(
        TenantContext tenant,
        string name,
        string deviceTypeId,
        string owningAgentId,
        string location,
        string brand,
        string model,
        string firmware,
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken = default)
    {
        if (!await IsValidOwningAgentAsync(tenant, owningAgentId, cancellationToken))
            return null;

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
        string status,
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken = default)
    {
        var entity = await _deviceRegistry.GetAsync(tenant.TenantId, tenant.SiteId, deviceId, cancellationToken);

        if (entity == null)
            return null;

        if (!await IsValidOwningAgentAsync(tenant, owningAgentId, cancellationToken))
            return null;

        var device = ToDomain(entity);
        device.Update(name, deviceTypeId, owningAgentId, location, brand, model, firmware, settings);
        device.SetStatus(Enum.Parse<DeviceStatus>(status));

        var updated = ToEntity(device);
        updated.ETag = entity.ETag;

        await _deviceRegistry.UpdateAsync(updated, cancellationToken);

        return ToDto(updated);
    }

    // Empty OwningAgentId means "not assigned yet" - always valid. A
    // non-empty one must resolve to a real Agent in this exact Tenant/Site
    // (ADR-058) - the authorization boundary "a Device's owning Agent must
    // belong to the same Tenant/Site" this ADR asked for.
    private async Task<bool> IsValidOwningAgentAsync(
        TenantContext tenant,
        string owningAgentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(owningAgentId))
            return true;

        var agent = await _agentRegistry.GetAsync(tenant.TenantId, tenant.SiteId, owningAgentId, cancellationToken);

        return agent != null;
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
            string.IsNullOrWhiteSpace(entity.Status) ? DeviceStatus.Active : Enum.Parse<DeviceStatus>(entity.Status),
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
            Status = device.Status.ToString(),
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
            string.IsNullOrWhiteSpace(entity.Status) ? DeviceStatus.Active.ToString() : entity.Status,
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
