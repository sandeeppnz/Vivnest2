namespace Vivnest.Cloud.Api.Dtos;

// Admin > Agent Installations record (decision-log.md ADR-053, lifecycle
// extended ADR-071). Status is now a real provisioning lifecycle
// (Pending/Installing/Installed/Updating/Active/Decommissioned), not just
// Active/Removed - see AgentInstallationStatus for the full state machine.
// VersionStatus is attached by the Function layer (decision-log.md
// ADR-073), the same "with { SyncStatus = ... }" pattern
// AgentRegistryAdminFunction already uses for config sync - null until
// attached, so it defaults to absent on any DTO built without that step.
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
    string SiteId,
    AgentVersionStatusDto? VersionStatus = null);

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

// Decision-log.md ADR-072 - the request body carries only the token; there
// is deliberately no tenant x-api-key on this route at all (see
// RegisterInstallation's own comment) - the token itself resolves tenant/
// site/installation.
public sealed record RegisterInstallationRequest(string InstallToken);

// StorageConnectionString is returned so a fresh Updater never needs an
// operator to hand-type it - Cloud already knows it (decision-log.md
// ADR-072). TenantId/SiteId are included so the Updater can pass them back
// unchanged on the deploy-complete callback, which otherwise has no way to
// resolve which tenant-scoped partition the installation lives in.
public sealed record AgentRegistrationResult(
    string RuntimeAgentId,
    string InstallationId,
    string TenantId,
    string SiteId,
    string? ImageVersion,
    string StorageConnectionString);

public sealed record ReportDeployCompleteRequest(string TenantId, string SiteId);
