using System.Security.Cryptography;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Auth;

// Decision-log.md ADR-071 - mirrors ApiKeyManagementService.CreateAsync
// exactly: a random secret, only its hash ever stored (PartitionKey, same
// as ApiKeyEntity), the raw value returned once and never retrievable
// again. Reuses ApiKeyHasher directly rather than a second hashing
// helper - same algorithm, same purpose (hash a bearer secret for
// lookup-without-storage), no reason for these to diverge.
public sealed class InstallTokenService : IInstallTokenService
{
    private const int TokenByteLength = 32;
    private const string InfoRowKey = "info";

    // Deliberately short - this token exists only to bridge the gap
    // between "admin created an installation" and "an operator actually
    // ran the Updater on the target Machine," not a long-lived credential.
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);

    private readonly IInstallationTokenStore _tokens;

    public InstallTokenService(IInstallationTokenStore tokens)
    {
        _tokens = tokens;
    }

    public async Task<InstallTokenCreationResult> CreateAsync(
        string tenantId,
        string siteId,
        string installationId,
        CancellationToken cancellationToken = default)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenByteLength));
        var createdUtc = DateTime.UtcNow;
        var expiresUtc = createdUtc + TokenLifetime;

        var entity = new AgentInstallationTokenEntity
        {
            PartitionKey = ApiKeyHasher.Hash(token),
            RowKey = InfoRowKey,
            TenantId = tenantId,
            SiteId = siteId,
            InstallationId = installationId,
            ExpiresUtc = expiresUtc,
            Used = false,
            CreatedUtc = createdUtc
        };

        await _tokens.CreateAsync(entity, cancellationToken);

        return new InstallTokenCreationResult(token, expiresUtc);
    }
}
