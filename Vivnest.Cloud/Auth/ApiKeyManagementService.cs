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
        CancellationToken cancellationToken = default)
    {
        var apiKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyByteLength));
        var createdUtc = DateTime.UtcNow;

        var entity = new ApiKeyEntity
        {
            PartitionKey = ApiKeyHasher.Hash(apiKey),
            RowKey = InfoRowKey,
            TenantId = tenantId,
            SiteId = siteId,
            Name = name,
            Enabled = true,
            CreatedUtc = createdUtc
        };

        await _apiKeys.CreateAsync(entity, cancellationToken);

        return new ApiKeyCreationResult(apiKey, createdUtc);
    }
}
