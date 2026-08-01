using System.Security.Cryptography;
using System.Text;
using Vivnest.Cloud.Interfaces;

namespace Vivnest.Cloud.Auth;

public sealed class ApiKeyAuthenticator : IApiKeyAuthenticator
{
    private readonly IApiKeyReader _apiKeys;

    public ApiKeyAuthenticator(IApiKeyReader apiKeys)
    {
        _apiKeys = apiKeys;
    }

    public async Task<TenantContext?> AuthenticateAsync(
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        var hash = Hash(apiKey);

        var entity = await _apiKeys.GetByHashAsync(hash, cancellationToken);

        if (entity is not { Enabled: true })
            return null;

        return new TenantContext(entity.TenantId, entity.SiteId);
    }

    private static string Hash(string apiKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(bytes);
    }
}
