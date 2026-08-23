using Vivnest.Core.DataStores.Entities;
using Vivnest.Cloud.Entities;

namespace Vivnest.Cloud.Auth;

// Decision-log.md ADR-071/ADR-072 - issues and validates the short-lived,
// single-use credential an AgentInstallation's registration flow uses.
public interface IInstallTokenService
{
    Task<InstallTokenCreationResult> CreateAsync(
        string tenantId,
        string siteId,
        string installationId,
        CancellationToken cancellationToken = default);

    // Decision-log.md ADR-072 - null covers every invalid case uniformly
    // (unknown token, already used, expired) - the registration endpoint
    // doesn't need to distinguish why a token didn't work, only that it
    // didn't. Marks the token Used on success so it can never be replayed,
    // even if the caller never actually finishes registering.
    Task<AgentInstallationTokenEntity?> ValidateAndConsumeAsync(
        string installToken,
        CancellationToken cancellationToken = default);
}
