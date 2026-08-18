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

    // Tenant/dashboard authentication, used by every route except the two
    // Agent-facing command callbacks.
    //
    // An *agent* key is deliberately rejected here. Agent keys are ordinary
    // rows in tblApiKeys with a non-null AgentId, so without this check a
    // key minted for one Agent would authenticate against /devices,
    // /agents and every admin route - handing each Agent a full tenant
    // credential, which would be a bigger hole than the unauthenticated
    // callbacks this mechanism exists to close. Scope in, then out.
    protected async Task<TenantContext?> AuthenticateAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAnyAsync(request, cancellationToken);

        return tenant?.AgentId is { Length: > 0 } ? null : tenant;
    }

    // Agent authentication: succeeds only for a key bound to an Agent.
    // The caller still has to check that it is bound to the *right* Agent.
    protected async Task<TenantContext?> AuthenticateAgentAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAnyAsync(request, cancellationToken);

        return tenant?.AgentId is { Length: > 0 } ? tenant : null;
    }

    private Task<TenantContext?> AuthenticateAnyAsync(
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
