using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Admin;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Functions;

// AuthorizationLevel.Function everywhere in this file, same reasoning as
// TenantsFunction/ApiKeysFunction - managing which Sites exist under a
// Tenant is a platform-operator action, not something a tenant-scoped
// x-api-key should ever reach.
public class SitesFunction
{
    private readonly ISiteManagementService _siteManagement;

    public SitesFunction(ISiteManagementService siteManagement)
    {
        _siteManagement = siteManagement;
    }

    [Function(nameof(ListSites))]
    public async Task<IActionResult> ListSites(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "tenants/{tenantId}/sites")]
            HttpRequest request,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var sites = await _siteManagement.GetByTenantAsync(tenantId, cancellationToken);

        return new OkObjectResult(sites);
    }

    [Function(nameof(GetSite))]
    public async Task<IActionResult> GetSite(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "tenants/{tenantId}/sites/{siteId}")]
            HttpRequest request,
        string tenantId,
        string siteId,
        CancellationToken cancellationToken)
    {
        var site = await _siteManagement.GetAsync(tenantId, siteId, cancellationToken);

        if (site == null)
            return new NotFoundResult();

        return new OkObjectResult(site);
    }

    [Function(nameof(CreateSite))]
    public async Task<IActionResult> CreateSite(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "tenants/{tenantId}/sites")]
            HttpRequest request,
        string tenantId,
        CancellationToken cancellationToken)
    {
        CreateSiteRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<CreateSiteRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.SiteId))
            return new BadRequestObjectResult("SiteId is required.");

        if (string.IsNullOrWhiteSpace(body.Name))
            return new BadRequestObjectResult("Name is required.");

        var site = await _siteManagement.CreateAsync(
            tenantId,
            body.SiteId,
            body.Name,
            body.Description,
            cancellationToken);

        if (site == null)
            return new ConflictObjectResult(
                $"Tenant \"{tenantId}\" doesn't exist, or Site \"{body.SiteId}\" already exists under it.");

        return new OkObjectResult(site);
    }

    [Function(nameof(UpdateSite))]
    public async Task<IActionResult> UpdateSite(
        [HttpTrigger(AuthorizationLevel.Function, "put", Route = "tenants/{tenantId}/sites/{siteId}")]
            HttpRequest request,
        string tenantId,
        string siteId,
        CancellationToken cancellationToken)
    {
        UpdateSiteRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<UpdateSiteRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.Name))
            return new BadRequestObjectResult("Name is required.");

        if (!Enum.TryParse<SiteStatus>(body.Status, out _))
            return new BadRequestObjectResult("Status must be one of: Active, Inactive.");

        var site = await _siteManagement.UpdateAsync(
            tenantId,
            siteId,
            body.Name,
            body.Description,
            body.Status,
            cancellationToken);

        if (site == null)
            return new NotFoundResult();

        return new OkObjectResult(site);
    }
}
