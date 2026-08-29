using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;

namespace Vivnest.Cloud.Functions.Http;

// Lets the dashboard discover its own key's permissions right after login,
// so it can hide UI (e.g. the Agents tab) a devices-only key can't use -
// the actual enforcement still lives server-side on each endpoint, this is
// purely so the UI doesn't have to guess or probe for it. Also resolves
// TenantName/SiteName (ADR-055's follow-up) - safe to look up here even
// though this is the tenant tier, not the operator tier: a key can only
// ever resolve to its own TenantId/SiteId, so revealing that Tenant/
// Site's own Name crosses no boundary the key doesn't already cross by
// returning the id itself.
public class WhoAmIFunction : ApiFunctionBase
{
    private readonly ITenantStore _tenants;
    private readonly ISiteStore _sites;

    public WhoAmIFunction(IApiKeyAuthenticator authenticator, ITenantStore tenants, ISiteStore sites)
        : base(authenticator)
    {
        _tenants = tenants;
        _sites = sites;
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

        var tenantEntity = await _tenants.GetAsync(tenant.TenantId, cancellationToken);
        var siteEntity = await _sites.GetAsync(tenant.TenantId, tenant.SiteId, cancellationToken);

        return new OkObjectResult(new WhoAmIResponse(
            tenant.TenantId,
            tenant.SiteId,
            tenant.DevicesOnly,
            tenant.IsDeveloper ? ApiKeyRoles.Developer : ApiKeyRoles.User,
            tenant.KeyName,
            tenantEntity?.Name,
            siteEntity?.Name));
    }
}
