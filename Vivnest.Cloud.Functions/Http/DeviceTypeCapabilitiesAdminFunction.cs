using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Functions.Http;

// Admin > Capability/DeviceType compatibility (decision-log.md ADR-062,
// Phase 5) - "Camera supports ObjectDetection." Same shape as
// CapabilityDependenciesAdminFunction - global data, tenant x-api-key
// auth is about who may call the admin API, not about the data being
// tenant-scoped.
public class DeviceTypeCapabilitiesAdminFunction : ApiFunctionBase
{
    private readonly ICapabilityCompatibilityService _compatibility;

    public DeviceTypeCapabilitiesAdminFunction(
        IApiKeyAuthenticator authenticator,
        ICapabilityCompatibilityService compatibility)
        : base(authenticator)
    {
        _compatibility = compatibility;
    }

    [Function(nameof(ListCompatibility))]
    public async Task<IActionResult> ListCompatibility(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "device-type-capabilities-admin")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var compatibility = await _compatibility.ListAllAsync(cancellationToken);

        return new OkObjectResult(compatibility);
    }

    [Function(nameof(AddCompatibility))]
    public async Task<IActionResult> AddCompatibility(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "device-type-capabilities-admin/add")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        AddDeviceTypeCapabilityRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<AddDeviceTypeCapabilityRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.DeviceTypeId))
            return new BadRequestObjectResult("DeviceTypeId is required.");

        if (string.IsNullOrWhiteSpace(body.CapabilityId))
            return new BadRequestObjectResult("CapabilityId is required.");

        var result = await _compatibility.AddAsync(body.DeviceTypeId, body.CapabilityId, cancellationToken);

        if (result.Error != null)
        {
            return result.Error switch
            {
                CapabilityCompatibilityError.DeviceTypeNotFound => new BadRequestObjectResult(result.ErrorMessage),
                CapabilityCompatibilityError.CapabilityNotFound => new BadRequestObjectResult(result.ErrorMessage),
                _ => new ConflictObjectResult(result.ErrorMessage)
            };
        }

        return new OkObjectResult(result.Compatibility);
    }

    [Function(nameof(RemoveCompatibility))]
    public async Task<IActionResult> RemoveCompatibility(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "device-type-capabilities-admin/{deviceTypeCapabilityId}")]
            HttpRequest request,
        string deviceTypeCapabilityId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var removed = await _compatibility.RemoveAsync(deviceTypeCapabilityId, cancellationToken);

        if (!removed)
            return new NotFoundResult();

        return new NoContentResult();
    }
}
