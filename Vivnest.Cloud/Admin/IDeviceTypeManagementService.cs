using Vivnest.Cloud.Api.Dtos;

namespace Vivnest.Cloud.Admin;

public interface IDeviceTypeManagementService
{
    Task<IReadOnlyList<DeviceTypeAdminDto>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<DeviceTypeAdminDto> CreateAsync(
        string deviceTypeName,
        CancellationToken cancellationToken = default);

    Task<DeviceTypeAdminDto?> UpdateAsync(
        string deviceTypeId,
        string deviceTypeName,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string deviceTypeId,
        CancellationToken cancellationToken = default);
}
