using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Domain.Devices;

namespace Vivnest.Cloud.Functions.Http;

// Admin > Device Types master-list CRUD (decision-log.md ADR-047/057).
// Same shape as CapabilitiesAdminFunction. Routed as "device-types-admin",
// not "admin/device-types" (reserved Azure Functions prefix, see
// CapabilitiesAdminFunction) and not "devices-*" (avoids any collision with
// the real tenant-facing "/devices*" routes or the "devices-registry-admin"
// routes).
public class DeviceTypesAdminFunction : ApiFunctionBase
{
    private readonly IDeviceTypeManagementService _deviceTypeManagement;

    public DeviceTypesAdminFunction(
        IApiKeyAuthenticator authenticator,
        IDeviceTypeManagementService deviceTypeManagement)
        : base(authenticator)
    {
        _deviceTypeManagement = deviceTypeManagement;
    }

    [Function(nameof(ListDeviceTypes))]
    public async Task<IActionResult> ListDeviceTypes(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "device-types-admin")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var deviceTypes = await _deviceTypeManagement.ListAsync(cancellationToken);

        return new OkObjectResult(deviceTypes);
    }

    [Function(nameof(CreateDeviceType))]
    public async Task<IActionResult> CreateDeviceType(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "device-types-admin")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        CreateDeviceTypeRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<CreateDeviceTypeRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.DeviceTypeName))
            return new BadRequestObjectResult("DeviceTypeName is required.");

        var deviceType = await _deviceTypeManagement.CreateAsync(
            body.DeviceTypeName,
            body.Description,
            cancellationToken);

        return new OkObjectResult(deviceType);
    }

    [Function(nameof(UpdateDeviceType))]
    public async Task<IActionResult> UpdateDeviceType(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "device-types-admin/{deviceTypeId}")]
            HttpRequest request,
        string deviceTypeId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        UpdateDeviceTypeRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<UpdateDeviceTypeRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.DeviceTypeName))
            return new BadRequestObjectResult("DeviceTypeName is required.");

        if (!Enum.TryParse<DeviceTypeStatus>(body.Status, out _))
            return new BadRequestObjectResult("Status must be one of: Active, Inactive.");

        var deviceType = await _deviceTypeManagement.UpdateAsync(
            deviceTypeId,
            body.DeviceTypeName,
            body.Description,
            body.Status,
            cancellationToken);

        if (deviceType == null)
            return new NotFoundResult();

        return new OkObjectResult(deviceType);
    }

    [Function(nameof(DeleteDeviceType))]
    public async Task<IActionResult> DeleteDeviceType(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "device-types-admin/{deviceTypeId}")]
            HttpRequest request,
        string deviceTypeId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var deleted = await _deviceTypeManagement.DeleteAsync(deviceTypeId, cancellationToken);

        if (!deleted)
            return new NotFoundResult();

        return new NoContentResult();
    }
}
