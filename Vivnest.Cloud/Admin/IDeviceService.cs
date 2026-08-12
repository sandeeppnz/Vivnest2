using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin;

// Application-layer service for the Device domain concept (decision-log.md
// ADR-057) - renamed from IDeviceRegistryManagementService now that Device
// is a real domain class, not just DeviceRegistryEntity built directly.
// The underlying table/entity (tblDeviceRegistry/DeviceRegistryEntity)
// keeps its existing name - persistence naming is a repository concern,
// independent of this rename, same reasoning that kept tblAgentRegistry's
// name when the Agent domain concept was discussed.
public interface IDeviceService
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
        IReadOnlyDictionary<string, string>? settings,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        TenantContext tenant,
        string deviceId,
        CancellationToken cancellationToken = default);
}
