using Microsoft.AspNetCore.Http;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Functions.Http;

public abstract class ApiFunctionBase
{
    private const string ApiKeyHeaderName = "x-api-key";
    private const int DefaultTake = 50;
    private const int MaxTake = 200;

    private readonly IApiKeyAuthenticator _authenticator;

    protected ApiFunctionBase(IApiKeyAuthenticator authenticator)
    {
        _authenticator = authenticator;
    }

    protected Task<TenantContext?> AuthenticateAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var apiKey = request.Headers[ApiKeyHeaderName].ToString();

        return _authenticator.AuthenticateAsync(apiKey, cancellationToken);
    }

    protected static int ParseTake(HttpRequest request)
    {
        if (request.Query.TryGetValue("take", out var raw)
            && int.TryParse(raw, out var parsed)
            && parsed > 0)
        {
            return Math.Min(parsed, MaxTake);
        }

        return DefaultTake;
    }
}
