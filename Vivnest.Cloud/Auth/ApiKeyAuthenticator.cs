using Vivnest.Cloud.Interfaces;

namespace Vivnest.Cloud.Auth;

public sealed class ApiKeyAuthenticator : IApiKeyAuthenticator
{
    private readonly IApiKeyStore _apiKeys;

    public ApiKeyAuthenticator(IApiKeyStore apiKeys)
    {
        _apiKeys = apiKeys;
    }

    public async Task<TenantContext?> AuthenticateAsync(
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        var hash = ApiKeyHasher.Hash(apiKey);

        var entity = await _apiKeys.GetByHashAsync(hash, cancellationToken);

        if (entity is not { Enabled: true })
            return null;

        return new TenantContext(entity.TenantId, entity.SiteId, entity.DevicesOnly, entity.AgentId);
    }
}
