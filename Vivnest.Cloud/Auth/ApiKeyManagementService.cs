using System.Security.Cryptography;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Domain.Agents;
using Vivnest.Domain.Sites;
using Vivnest.Domain.Tenants;

namespace Vivnest.Cloud.Auth;

// Validates TenantId/SiteId against the real Tenant/Site stores at create
// time (ADR-052) - not possible before ADR-051 gave Tenant/Site a real
// table to check against. Deliberately only gates creation: ListAsync/
// RevokeAsync/ApiKeyAuthenticator all still resolve purely off the key's
// own denormalized TenantId/SiteId fields, so this doesn't affect any
// key created before this check existed.
public sealed class ApiKeyManagementService : IApiKeyManagementService
{
    private const int KeyByteLength = 32;
    private const string InfoRowKey = "info";

    private readonly IApiKeyStore _apiKeys;
    private readonly ITenantStore _tenants;
    private readonly ISiteStore _sites;

    public ApiKeyManagementService(IApiKeyStore apiKeys, ITenantStore tenants, ISiteStore sites)
    {
        _apiKeys = apiKeys;
        _tenants = tenants;
        _sites = sites;
    }

    public async Task<ApiKeyCreationResult?> CreateAsync(
        string tenantId,
        string siteId,
        string? name,
        bool devicesOnly,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenants.GetAsync(tenantId, cancellationToken);

        if (tenant == null || tenant.Status != TenantStatus.Active.ToString())
            return null;

        var site = await _sites.GetAsync(tenantId, siteId, cancellationToken);

        if (site == null || site.Status != SiteStatus.Active.ToString())
            return null;

        var apiKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyByteLength));
        var keyId = Guid.NewGuid().ToString();
        var createdUtc = DateTime.UtcNow;

        var entity = new ApiKeyEntity
        {
            PartitionKey = ApiKeyHasher.Hash(apiKey),
            RowKey = InfoRowKey,
            TenantId = tenantId,
            SiteId = siteId,
            Name = name,
            KeyId = keyId,
            Enabled = true,
            DevicesOnly = devicesOnly,
            CreatedUtc = createdUtc
        };

        await _apiKeys.CreateAsync(entity, cancellationToken);

        return new ApiKeyCreationResult(keyId, apiKey, createdUtc);
    }

    public async Task<ApiKeyCreationResult?> CreateForAgentAsync(
        string tenantId,
        string siteId,
        string runtimeAgentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runtimeAgentId))
            return null;

        // Deliberately skips CreateAsync's Tenant/Site Active checks: the
        // caller is RegisterAsync, which has already validated a
        // single-use install token issued against this exact Tenant/Site.
        // Re-reading those rows here would add two round trips to the
        // registration handshake for a condition already established.
        var apiKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyByteLength));
        var createdUtc = DateTime.UtcNow;

        var entity = new ApiKeyEntity
        {
            PartitionKey = ApiKeyHasher.Hash(apiKey),
            RowKey = InfoRowKey,
            TenantId = tenantId,
            SiteId = siteId,
            Name = $"agent:{runtimeAgentId}",
            KeyId = Guid.NewGuid().ToString(),
            Enabled = true,
            DevicesOnly = false,
            AgentId = runtimeAgentId,
            CreatedUtc = createdUtc
        };

        await _apiKeys.CreateAsync(entity, cancellationToken);

        return new ApiKeyCreationResult(entity.KeyId, apiKey, createdUtc);
    }

    public async Task<ApiKeyCreationResult?> IssueForExistingAgentAsync(
        string tenantId,
        string siteId,
        string runtimeAgentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runtimeAgentId))
            return null;

        // Revoke first, not after. If the mint succeeds and the revoke
        // then fails, two keys would be live for one Agent with no signal;
        // this order can at worst leave the Agent with none, which is the
        // safe direction - it degrades to the grace path rather than
        // silently widening access.
        var existing = await _apiKeys.GetByTenantAsync(tenantId, siteId, cancellationToken);

        foreach (var key in existing.Where(k =>
                     k.Enabled && string.Equals(k.AgentId, runtimeAgentId, StringComparison.Ordinal)))
        {
            key.Enabled = false;
            await _apiKeys.UpdateAsync(key, cancellationToken);
        }

        return await CreateForAgentAsync(tenantId, siteId, runtimeAgentId, cancellationToken);
    }

    public async Task<IReadOnlyList<ApiKeySummary>> ListAsync(
        string tenantId,
        string siteId,
        CancellationToken cancellationToken = default)
    {
        var entities = await _apiKeys.GetByTenantAsync(tenantId, siteId, cancellationToken);

        return entities
            .Select(e => new ApiKeySummary(
                e.KeyId,
                e.Name,
                e.TenantId,
                e.SiteId,
                e.Enabled,
                e.DevicesOnly,
                e.CreatedUtc))
            .ToList();
    }

    public async Task<bool> RevokeAsync(
        string keyId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _apiKeys.GetByKeyIdAsync(keyId, cancellationToken);

        if (entity == null)
            return false;

        entity.Enabled = false;

        await _apiKeys.UpdateAsync(entity, cancellationToken);

        return true;
    }
}
