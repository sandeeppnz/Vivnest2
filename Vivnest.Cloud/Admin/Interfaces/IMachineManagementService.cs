using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin.Interfaces;

public interface IMachineManagementService
{
    Task<IReadOnlyList<MachineDto>> ListAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default);

    Task<MachineDto?> GetAsync(
        TenantContext tenant,
        string machineId,
        CancellationToken cancellationToken = default);

    // MachineId is a generated Guid - no collision possible, so no
    // nullable/conflict return here (same reasoning as
    // IAgentRegistryManagementService.CreateAsync).
    Task<MachineDto> CreateAsync(
        TenantContext tenant,
        string name,
        string? hostname,
        string? description,
        string? operatingSystem,
        string? architecture,
        CancellationToken cancellationToken = default);

    Task<MachineDto?> UpdateAsync(
        TenantContext tenant,
        string machineId,
        string name,
        string? hostname,
        string? description,
        string status,
        string? operatingSystem,
        string? architecture,
        CancellationToken cancellationToken = default);
}
