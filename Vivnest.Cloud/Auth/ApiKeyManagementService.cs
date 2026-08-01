using System.Security.Cryptography;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.DataStores.Entities;

namespace Vivnest.Cloud.Auth;

public sealed class ApiKeyManagementService : IApiKeyManagementService
{
    private const int KeyByteLength = 32;
    private const string InfoRowKey = "info";

    private readonly IApiKeyStore _apiKeys;

    public ApiKeyManagementService(IApiKeyStore apiKeys)
    {
        _apiKeys = apiKeys;
    }

    public async Task<ApiKeyCreationResult> CreateAsync(
        string tenantId,
        string siteId,
        string? name,
        bool devicesOnly,
        CancellationToken cancellationToken = default)
    {
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
