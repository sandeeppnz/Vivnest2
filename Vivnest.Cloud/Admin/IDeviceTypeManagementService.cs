using Vivnest.Cloud.Api.Dtos;

namespace Vivnest.Cloud.Admin;

public interface IDeviceTypeManagementService
{
    Task<IReadOnlyList<DeviceTypeAdminDto>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<DeviceTypeAdminDto> CreateAsync(
        string deviceTypeName,
        string? description,
        CancellationToken cancellationToken = default);

    Task<DeviceTypeAdminDto?> UpdateAsync(
        string deviceTypeId,
        string deviceTypeName,
        string? description,
        string status,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string deviceTypeId,
        CancellationToken cancellationToken = default);
}
