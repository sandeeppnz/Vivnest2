using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin;

public interface IMachineManagementService
{
    Task<IReadOnlyList<MachineDto>> ListAsync(
        TenantContext tenant,
        CancellationToken cancellationToken = default);

    Task<MachineDto?> GetAsync(
        TenantContext tenant,
        string machineId,
        CancellationToken cancellationToken = default);

    // Returns null if MachineId already exists for this tenant/site - a
    // genuine conflict, not a silent overwrite, since MachineId is a
    // caller-chosen id (same reasoning as ITenantManagementService.CreateAsync).
    Task<MachineDto?> CreateAsync(
        TenantContext tenant,
        string machineId,
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
