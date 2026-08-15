namespace Vivnest.Cloud.Auth;

// Decision-log.md ADR-071 - issues the short-lived, single-use credential
// an AgentInstallation's registration flow (Pass 2) will validate. Create
// only for now - validation/consumption lands in Pass 2 alongside the
// registration endpoint that's the only real caller of it.
public interface IInstallTokenService
{
    Task<InstallTokenCreationResult> CreateAsync(
        string tenantId,
        string siteId,
        string installationId,
        CancellationToken cancellationToken = default);
}
