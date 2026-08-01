namespace Vivnest.Cloud.Auth;

public interface IApiKeyAuthenticator
{
    Task<TenantContext?> AuthenticateAsync(
        string? apiKey,
        CancellationToken cancellationToken = default);
}
