using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Domain.Devices;

namespace Vivnest.Cloud.Admin;

public sealed class CapabilityCompatibilityService : ICapabilityCompatibilityService
{
    private readonly IDeviceTypeCapabilityStore _compatibility;
    private readonly ICapabilityStore _capabilities;
    private readonly IDeviceTypeStore _deviceTypes;

    public CapabilityCompatibilityService(
        IDeviceTypeCapabilityStore compatibility,
        ICapabilityStore capabilities,
        IDeviceTypeStore deviceTypes)
    {
        _compatibility = compatibility;
        _capabilities = capabilities;
        _deviceTypes = deviceTypes;
    }

    public async Task<IReadOnlyList<DeviceTypeCapabilityDto>> ListAllAsync(
        CancellationToken cancellationToken = default)
    {
        var entities = await _compatibility.ListAsync(cancellationToken);

        return entities.Select(ToDto).ToList();
    }

    public async Task<CapabilityCompatibilityResult> AddAsync(
        string deviceTypeId,
        string capabilityId,
        CancellationToken cancellationToken = default)
    {
        var deviceType = await _deviceTypes.GetAsync(deviceTypeId, cancellationToken);

        if (deviceType == null)
        {
            return new CapabilityCompatibilityResult(
                null, CapabilityCompatibilityError.DeviceTypeNotFound, $"DeviceTypeId \"{deviceTypeId}\" doesn't exist.");
        }

        var capability = await _capabilities.GetAsync(capabilityId, cancellationToken);

        if (capability == null)
        {
            return new CapabilityCompatibilityResult(
                null, CapabilityCompatibilityError.CapabilityNotFound, $"CapabilityId \"{capabilityId}\" doesn't exist.");
        }

        var existing = await _compatibility.ListByDeviceTypeAsync(deviceTypeId, cancellationToken);

        if (existing.Any(c => c.CapabilityId == capabilityId))
        {
            return new CapabilityCompatibilityResult(
                null,
                CapabilityCompatibilityError.AlreadyExists,
                "This capability is already compatible with this device type.");
        }

        var compatibility = new DeviceTypeCapability(deviceTypeId, capabilityId);
        var entity = ToEntity(compatibility);

        await _compatibility.CreateAsync(entity, cancellationToken);

        return new CapabilityCompatibilityResult(ToDto(entity), null, null);
    }

    public async Task<bool> RemoveAsync(
        string deviceTypeCapabilityId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _compatibility.GetAsync(deviceTypeCapabilityId, cancellationToken);

        if (entity == null)
            return false;

        await _compatibility.DeleteAsync(deviceTypeCapabilityId, cancellationToken);

        return true;
    }

    private static DeviceTypeCapabilityEntity ToEntity(DeviceTypeCapability compatibility)
    {
        return new DeviceTypeCapabilityEntity
        {
            RowKey = compatibility.DeviceTypeCapabilityId,
            DeviceTypeId = compatibility.DeviceTypeId,
            CapabilityId = compatibility.CapabilityId
        };
    }

    private static DeviceTypeCapabilityDto ToDto(DeviceTypeCapabilityEntity entity)
    {
        return new DeviceTypeCapabilityDto(
            Guid.Parse(entity.RowKey),
            Guid.Parse(entity.DeviceTypeId),
            Guid.Parse(entity.CapabilityId));
    }
}
