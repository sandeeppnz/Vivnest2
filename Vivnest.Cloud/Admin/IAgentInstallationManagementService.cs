using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin;

public interface IAgentInstallationManagementService
{
    Task<IReadOnlyList<AgentInstallationDto>> GetByAgentAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentInstallationDto>> GetByMachineAsync(
        TenantContext tenant,
        string machineId,
        CancellationToken cancellationToken = default);

    Task<AgentInstallationDto?> GetActiveByAgentAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentInstallationDto>> GetActiveByMachineAsync(
        TenantContext tenant,
        string machineId,
        CancellationToken cancellationToken = default);

    // Returns null if AgentId doesn't exist, MachineId doesn't exist, or
    // the Agent already has an active installation (must Move or
    // Uninstall first - see decision-log.md ADR-053's "at most one active
    // installation per Agent" invariant).
    Task<AgentInstallationDto?> InstallAsync(
        TenantContext tenant,
        string agentId,
        string machineId,
        string? containerId,
        string? imageName,
        string? imageVersion,
        CancellationToken cancellationToken = default);

    // Retires the Agent's current active installation (if any) and
    // creates a new one on the given Machine - the old installation is
    // never mutated to point at the new Machine, preserving history.
    // Returns null if AgentId or the new MachineId doesn't exist.
    Task<AgentInstallationDto?> MoveAsync(
        TenantContext tenant,
        string agentId,
        string machineId,
        string? containerId,
        string? imageName,
        string? imageVersion,
        CancellationToken cancellationToken = default);

    // Returns null if the Agent has no active installation.
    Task<AgentInstallationDto?> UninstallAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default);
}
