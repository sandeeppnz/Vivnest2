using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Functions.Http;

// Lets the dashboard discover its own key's permissions right after login,
// so it can hide UI (e.g. the Agents tab) a devices-only key can't use -
// the actual enforcement still lives server-side on each endpoint, this is
// purely so the UI doesn't have to guess or probe for it.
public class WhoAmIFunction : ApiFunctionBase
{
    public WhoAmIFunction(IApiKeyAuthenticator authenticator)
        : base(authenticator)
    {
    }

    [Function(nameof(WhoAmI))]
    public async Task<IActionResult> WhoAmI(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "whoami")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        return new OkObjectResult(new WhoAmIResponse(
            tenant.TenantId,
            tenant.SiteId,
            tenant.DevicesOnly));
    }
}
