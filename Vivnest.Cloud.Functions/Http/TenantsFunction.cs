using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Functions.Http;

// AuthorizationLevel.Function everywhere in this file, same reasoning as
// ApiKeysFunction - creating/listing/updating Tenants is a platform-
// operator action, not something a tenant-scoped x-api-key should ever be
// able to do. A tenant key is scoped to exactly one Tenant/Site; if these
// routes accepted one, any tenant could list or create every other tenant,
// which is exactly the cross-tenant boundary violation the Tenant/Site
// model exists to prevent.
public class TenantsFunction
{
    private readonly ITenantManagementService _tenantManagement;

    public TenantsFunction(ITenantManagementService tenantManagement)
    {
        _tenantManagement = tenantManagement;
    }

    [Function(nameof(ListTenants))]
    public async Task<IActionResult> ListTenants(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "tenants")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenants = await _tenantManagement.ListAsync(cancellationToken);

        return new OkObjectResult(tenants);
    }

    [Function(nameof(GetTenant))]
    public async Task<IActionResult> GetTenant(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "tenants/{tenantId}")]
            HttpRequest request,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantManagement.GetAsync(tenantId, cancellationToken);

        if (tenant == null)
            return new NotFoundResult();

        return new OkObjectResult(tenant);
    }

    [Function(nameof(CreateTenant))]
    public async Task<IActionResult> CreateTenant(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "tenants")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        CreateTenantRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<CreateTenantRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.Name))
            return new BadRequestObjectResult("Name is required.");

        var tenant = await _tenantManagement.CreateAsync(
            body.Name,
            body.Description,
            cancellationToken);

        return new OkObjectResult(tenant);
    }

    [Function(nameof(UpdateTenant))]
    public async Task<IActionResult> UpdateTenant(
        [HttpTrigger(AuthorizationLevel.Function, "put", Route = "tenants/{tenantId}")]
            HttpRequest request,
        string tenantId,
        CancellationToken cancellationToken)
    {
        UpdateTenantRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<UpdateTenantRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.Name))
            return new BadRequestObjectResult("Name is required.");

        if (!Enum.TryParse<TenantStatus>(body.Status, out _))
            return new BadRequestObjectResult("Status must be one of: Active, Inactive.");

        var tenant = await _tenantManagement.UpdateAsync(
            tenantId,
            body.Name,
            body.Description,
            body.Status,
            cancellationToken);

        if (tenant == null)
            return new NotFoundResult();

        return new OkObjectResult(tenant);
    }

    // Soft delete - sets Status to Inactive, same as PUT with
    // {"Status": "Inactive"}. No hard delete - see
    // ITenantManagementService.DeactivateAsync.
    [Function(nameof(DeleteTenant))]
    public async Task<IActionResult> DeleteTenant(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "tenants/{tenantId}")]
            HttpRequest request,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantManagement.DeactivateAsync(tenantId, cancellationToken);

        if (tenant == null)
            return new NotFoundResult();

        return new OkObjectResult(tenant);
    }
}
