using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Admin;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Functions.Http;

// Admin > Devices pre-registration CRUD (decision-log.md ADR-048). Same
// shape as AgentRegistryAdminFunction. Routed as "devices-registry-admin",
// not "admin/devices" (reserved prefix) and not literally "/devices" (the
// real tenant-facing route).
//
// Settings accepts any key, including credentials (decision-log.md
// ADR-050 - by direct request, reversing ADR-048's original guard). This
// is a real departure from every other credential in this codebase, which
// stays local-only in *.secrets.json files (ADR-038): anything saved here
// is stored as plain text in tblDeviceRegistry and returned as plain text
// by ListDeviceRegistry to any caller holding a valid tenant x-api-key.
public class DeviceRegistryAdminFunction : ApiFunctionBase
{
    private readonly IDeviceRegistryManagementService _deviceRegistryManagement;

    public DeviceRegistryAdminFunction(
        IApiKeyAuthenticator authenticator,
        IDeviceRegistryManagementService deviceRegistryManagement)
        : base(authenticator)
    {
        _deviceRegistryManagement = deviceRegistryManagement;
    }

    [Function(nameof(ListDeviceRegistry))]
    public async Task<IActionResult> ListDeviceRegistry(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "devices-registry-admin")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var devices = await _deviceRegistryManagement.ListAsync(tenant, cancellationToken);

        return new OkObjectResult(devices);
    }

    [Function(nameof(CreateDeviceRegistry))]
    public async Task<IActionResult> CreateDeviceRegistry(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "devices-registry-admin")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        CreateDeviceRegistryRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<CreateDeviceRegistryRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.Name))
            return new BadRequestObjectResult("Name is required.");

        var device = await _deviceRegistryManagement.CreateAsync(
            tenant,
            body.Name,
            body.DeviceTypeId,
            body.OwningAgentId,
            body.Location,
            body.Brand,
            body.Model,
            body.Firmware,
            body.Enabled,
            body.CapabilityIds,
            body.Settings,
            cancellationToken);

        return new OkObjectResult(device);
    }

    [Function(nameof(UpdateDeviceRegistry))]
    public async Task<IActionResult> UpdateDeviceRegistry(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "devices-registry-admin/{deviceId}")]
            HttpRequest request,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        UpdateDeviceRegistryRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<UpdateDeviceRegistryRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.Name))
            return new BadRequestObjectResult("Name is required.");

        var device = await _deviceRegistryManagement.UpdateAsync(
            tenant,
            deviceId,
            body.Name,
            body.DeviceTypeId,
            body.OwningAgentId,
            body.Location,
            body.Brand,
            body.Model,
            body.Firmware,
            body.Enabled,
            body.CapabilityIds,
            body.Settings,
            cancellationToken);

        if (device == null)
            return new NotFoundResult();

        return new OkObjectResult(device);
    }

    [Function(nameof(DeleteDeviceRegistry))]
    public async Task<IActionResult> DeleteDeviceRegistry(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "devices-registry-admin/{deviceId}")]
            HttpRequest request,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var deleted = await _deviceRegistryManagement.DeleteAsync(tenant, deviceId, cancellationToken);

        if (!deleted)
            return new NotFoundResult();

        return new NoContentResult();
    }
}
