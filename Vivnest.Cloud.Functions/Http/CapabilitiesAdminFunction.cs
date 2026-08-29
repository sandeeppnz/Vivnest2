using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Domain.Capabilities;
using Vivnest.Domain.Devices;
using Vivnest.Domain.Tenants;

namespace Vivnest.Cloud.Functions.Http;

// Admin > Capabilities master-list CRUD (decision-log.md ADR-042). Tenant
// x-api-key via ApiFunctionBase, not ApiKeysFunction's operator-tier
// AuthorizationLevel.Function - the dashboard has no mechanism to hold a
// host key, and gaining one just for this screen was explicitly rejected.
// Routed as "capabilities-admin", not "admin/capabilities" - a literal
// "admin/" path segment collides with Azure Functions' own reserved
// built-in admin API routes (confirmed by a real startup failure:
// "The specified route conflicts with one or more built in routes").
public class CapabilitiesAdminFunction : ApiFunctionBase
{
    private readonly ICapabilityManagementService _capabilityManagement;
    private readonly ICatalogueSeedService _catalogueSeed;

    public CapabilitiesAdminFunction(
        IApiKeyAuthenticator authenticator,
        ICapabilityManagementService capabilityManagement,
        ICatalogueSeedService catalogueSeed)
        : base(authenticator)
    {
        _capabilityManagement = capabilityManagement;
        _catalogueSeed = catalogueSeed;
    }

    // Catalogue self-seeding (ADR-119): reconciles capabilities, device
    // types and compatibility links against the in-code CatalogueSeed
    // manifest. Idempotent - safe to call any number of times; repairs
    // identity drift (the projector-bound name / adapter-bound key)
    // without touching admin-customized schemas.
    [Function(nameof(SeedCatalogue))]
    public async Task<IActionResult> SeedCatalogue(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "capabilities-admin/seed")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (!tenant.IsDeveloper)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var report = await _catalogueSeed.SeedAsync(cancellationToken);

        return new OkObjectResult(report);
    }

    [Function(nameof(ListCapabilities))]
    public async Task<IActionResult> ListCapabilities(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "capabilities-admin")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (!tenant.IsDeveloper)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var capabilities = await _capabilityManagement.ListAsync(cancellationToken);

        return new OkObjectResult(capabilities);
    }

    [Function(nameof(CreateCapability))]
    public async Task<IActionResult> CreateCapability(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "capabilities-admin")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (!tenant.IsDeveloper)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        CreateCapabilityRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<CreateCapabilityRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.CapabilityName))
            return new BadRequestObjectResult("CapabilityName is required.");

        if (!Enum.TryParse<CapabilityType>(body.CapabilityType, out _))
            return new BadRequestObjectResult("CapabilityType must be one of: Device, Service, System.");

        var capability = await _capabilityManagement.CreateAsync(
            body.CapabilityName,
            body.CapabilityType,
            body.ConfigurationSchema,
            body.ConfigurationSchemaVersion,
            body.DefaultConfiguration,
            body.CapabilityKey,
            cancellationToken);

        return new OkObjectResult(capability);
    }

    [Function(nameof(UpdateCapability))]
    public async Task<IActionResult> UpdateCapability(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "capabilities-admin/{capabilityId}")]
            HttpRequest request,
        string capabilityId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (!tenant.IsDeveloper)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        UpdateCapabilityRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<UpdateCapabilityRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.CapabilityName))
            return new BadRequestObjectResult("CapabilityName is required.");

        if (!Enum.TryParse<CapabilityType>(body.CapabilityType, out _))
            return new BadRequestObjectResult("CapabilityType must be one of: Device, Service, System.");

        if (!Enum.TryParse<CapabilityStatus>(body.Status, out _))
            return new BadRequestObjectResult("Status must be one of: Active, Retired.");

        var capability = await _capabilityManagement.UpdateAsync(
            capabilityId,
            body.CapabilityName,
            body.CapabilityType,
            body.Status,
            body.ConfigurationSchema,
            body.ConfigurationSchemaVersion,
            body.DefaultConfiguration,
            body.CapabilityKey,
            cancellationToken);

        if (capability == null)
            return new NotFoundResult();

        return new OkObjectResult(capability);
    }

    [Function(nameof(DeleteCapability))]
    public async Task<IActionResult> DeleteCapability(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "capabilities-admin/{capabilityId}")]
            HttpRequest request,
        string capabilityId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (!tenant.IsDeveloper)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var result = await _capabilityManagement.DeleteAsync(capabilityId, cancellationToken);

        if (result.Error == CapabilityDeleteError.NotFound)
            return new NotFoundResult();

        if (result.Error == CapabilityDeleteError.Referenced)
            return new ConflictObjectResult(result.ErrorMessage);

        return new NoContentResult();
    }
}
