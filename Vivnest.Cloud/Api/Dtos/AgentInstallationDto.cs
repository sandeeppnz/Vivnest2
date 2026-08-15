namespace Vivnest.Cloud.Api.Dtos;

// Admin > Agent Installations record (decision-log.md ADR-053, lifecycle
// extended ADR-071). Status is now a real provisioning lifecycle
// (Pending/Installing/Installed/Updating/Active/Decommissioned), not just
// Active/Removed - see AgentInstallationStatus for the full state machine.
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

// Decision-log.md ADR-071 - InstallToken is returned exactly once, the
// same "one-time reveal" convention CreateApiKeyResponse already
// established; there is no endpoint that can retrieve it again after this
// response.
public sealed record AgentInstallationCreationResult(
    AgentInstallationDto Installation,
    string InstallToken,
    DateTime InstallTokenExpiresUtc);
