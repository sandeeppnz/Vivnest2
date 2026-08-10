using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Admin;

// A genuine hard delete, same reasoning as CapabilityManagementService -
// this is master/reference data meant to actually shrink, not an audit
// trail. No existence validation against Vivnest.Core.Enums.DeviceType or
// against any device/agent registry entry referencing a deleted type -
// same no-FK-validation convention as CapabilityIds/OwningAgentId elsewhere
// in this codebase.
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
        CancellationToken cancellationToken = default)
    {
        var entity = new DeviceTypeEntity
        {
            RowKey = Guid.NewGuid().ToString(),
            DeviceTypeName = deviceTypeName
        };

        await _deviceTypes.CreateAsync(entity, cancellationToken);

        return ToDto(entity);
    }

    public async Task<DeviceTypeAdminDto?> UpdateAsync(
        string deviceTypeId,
        string deviceTypeName,
        CancellationToken cancellationToken = default)
    {
        var entity = await _deviceTypes.GetAsync(deviceTypeId, cancellationToken);

        if (entity == null)
            return null;

        entity.DeviceTypeName = deviceTypeName;

        await _deviceTypes.UpdateAsync(entity, cancellationToken);

        return ToDto(entity);
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

    private static DeviceTypeAdminDto ToDto(DeviceTypeEntity entity)
    {
        return new DeviceTypeAdminDto(
            Guid.Parse(entity.RowKey),
            entity.DeviceTypeName);
    }
}
