namespace Vivnest.Cloud.Api.Dtos;

// Admin > Agent Installations record (decision-log.md ADR-053) - a
// specific deployment of an Agent onto a Machine. Purely a declarative/
// administrative record for this phase, not wired to the real
// Vivnest.Agent.Updater deploy pipeline - see ADR-053 for why.
public sealed record AgentInstallationDto(
    string InstallationId,
    string AgentId,
    string MachineId,
    string? ContainerId,
    string? ImageName,
    string? ImageVersion,
    string Status,
    DateTime InstalledUtc,
    DateTime? RemovedUtc,
    DateTime UpdatedUtc,
    string TenantId,
    string SiteId);

public sealed record InstallAgentRequest(
    string AgentId,
    string MachineId,
    string? ContainerId,
    string? ImageName,
    string? ImageVersion);

public sealed record MoveAgentRequest(
    string AgentId,
    string MachineId,
    string? ContainerId,
    string? ImageName,
    string? ImageVersion);

public sealed record UninstallAgentRequest(
    string AgentId);
