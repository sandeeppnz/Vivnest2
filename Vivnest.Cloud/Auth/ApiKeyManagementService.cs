using System.Security.Cryptography;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;
using Vivnest.Core.Enums;

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
