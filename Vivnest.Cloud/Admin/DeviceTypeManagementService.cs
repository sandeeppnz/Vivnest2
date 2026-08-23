using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Domain.Devices;
using Vivnest.Domain.Machines;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Admin;

// Maps the persistence-agnostic DeviceTypeDefinition domain model
// (Vivnest.Core.Domain) to/from DeviceTypeEntity for storage (decision-log.md
// ADR-057) - same
// shape MachineManagementService already established for Machine. A
// genuine hard delete, same reasoning as CapabilityManagementService -
// this is master/reference data meant to actually shrink, not an audit
// trail. No existence validation against any device/agent registry entry
// referencing a deleted type - same no-FK-validation convention as
// CapabilityIds/OwningAgentId elsewhere in this codebase.
public sealed class DeviceTypeManagementService : IDeviceTypeManagementService
{
    private readonly IDeviceTypeStore _deviceTypes;

    public DeviceTypeManagementService(IDeviceTypeStore deviceTypes)
    {
        _deviceTypes = deviceTypes;
    }

    public async Task<IReadOnlyList<DeviceTypeAdminDto>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var entities = await _deviceTypes.ListAsync(cancellationToken);

        return entities.Select(ToDto).ToList();
    }

    public async Task<DeviceTypeAdminDto> CreateAsync(
        string deviceTypeName,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var deviceType = new DeviceTypeDefinition(deviceTypeName, description);

        var entity = ToEntity(deviceType);

        await _deviceTypes.CreateAsync(entity, cancellationToken);

        return ToDto(entity);
    }

    public async Task<DeviceTypeAdminDto?> UpdateAsync(
        string deviceTypeId,
        string deviceTypeName,
        string? description,
        string status,
        CancellationToken cancellationToken = default)
    {
        var entity = await _deviceTypes.GetAsync(deviceTypeId, cancellationToken);

        if (entity == null)
            return null;

        var deviceType = ToDomain(entity);
        deviceType.Update(deviceTypeName, description);
        deviceType.SetStatus(Enum.Parse<DeviceTypeStatus>(status));

        var updated = ToEntity(deviceType);
        updated.ETag = entity.ETag;

        await _deviceTypes.UpdateAsync(updated, cancellationToken);

        return ToDto(updated);
    }

    public async Task<bool> DeleteAsync(
        string deviceTypeId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _deviceTypes.GetAsync(deviceTypeId, cancellationToken);

        if (entity == null)
            return false;

        await _deviceTypes.DeleteAsync(deviceTypeId, cancellationToken);

        return true;
    }

    private static DeviceTypeDefinition ToDomain(DeviceTypeEntity entity)
    {
        return DeviceTypeDefinition.Rehydrate(
            entity.RowKey,
            entity.DeviceTypeName,
            entity.Description,
            Enum.Parse<DeviceTypeStatus>(entity.Status),
            entity.CreatedUtc,
            entity.UpdatedUtc);
    }

    private static DeviceTypeEntity ToEntity(DeviceTypeDefinition deviceType)
    {
        return new DeviceTypeEntity
        {
            RowKey = deviceType.DeviceTypeId,
            DeviceTypeName = deviceType.Name,
            Description = deviceType.Description,
            Status = deviceType.Status.ToString(),
            CreatedUtc = deviceType.CreatedUtc,
            UpdatedUtc = deviceType.UpdatedUtc
        };
    }

    private static DeviceTypeAdminDto ToDto(DeviceTypeEntity entity)
    {
        return new DeviceTypeAdminDto(
            Guid.Parse(entity.RowKey),
            entity.DeviceTypeName,
            entity.Description,
            entity.Status,
            entity.CreatedUtc,
            entity.UpdatedUtc);
    }
}
