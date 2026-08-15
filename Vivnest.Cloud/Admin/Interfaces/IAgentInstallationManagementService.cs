using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin.Interfaces;

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
    // installation per Agent" invariant). Decision-log.md ADR-071 - the
    // new installation always starts Pending, with a one-time install
    // token issued alongside it (never retrievable again after this
    // call) for whoever provisions the target Machine to register with.
    Task<AgentInstallationCreationResult?> InstallAsync(
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
    // Returns null if AgentId or the new MachineId doesn't exist. Same
    // Pending + one-time install token as InstallAsync - a Move targets a
    // genuinely different Machine (e.g. hardware replacement), which
    // needs its own registration just like a first-ever install.
    Task<AgentInstallationCreationResult?> MoveAsync(
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
