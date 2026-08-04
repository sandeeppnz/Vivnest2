using Microsoft.AspNetCore.Http;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Functions.Http;

// Every [HttpTrigger] in this API uses AuthorizationLevel.Anonymous - that
// only disables Azure Functions' own platform-level key gate
// (?code=.../x-functions-key), not authentication. The real auth is
// AuthenticateAsync below, called first thing in every handler: it checks
// the x-api-key header against tblApiKeys (ADR-012's tenant-key/host-key
// model) and every endpoint returns 401/403 itself when that fails. Easy
// to misread "Anonymous" as "no auth" at a glance - it isn't.
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
