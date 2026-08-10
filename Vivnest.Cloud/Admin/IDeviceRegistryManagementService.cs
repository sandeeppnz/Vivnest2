using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin;

public interface IDeviceRegistryManagementService
{
    Task<IReadOnlyList<DeviceRegistryDto>> ListAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default);

    Task<DeviceRegistryDto> CreateAsync(
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
        CancellationToken cancellationToken = default);

    Task<DeviceRegistryDto?> UpdateAsync(
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
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        TenantContext tenant,
        string deviceId,
        CancellationToken cancellationToken = default);
}
