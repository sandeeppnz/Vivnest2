using Vivnest.Cloud.Api.Dtos;

namespace Vivnest.Cloud.Admin;

public interface ICapabilityManagementService
{
    Task<IReadOnlyList<CapabilityAdminDto>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<CapabilityAdminDto> CreateAsync(
        string capabilityName,
        string capabilityType,
        CancellationToken cancellationToken = default);

    Task<CapabilityAdminDto?> UpdateAsync(
        string capabilityId,
        string capabilityName,
        string capabilityType,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string capabilityId,
        CancellationToken cancellationToken = default);
}
