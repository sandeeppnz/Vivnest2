using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Admin;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Functions.Http;

// Admin > Devices pre-registration CRUD (decision-log.md ADR-048/057/058).
// Same shape as AgentRegistryAdminFunction. Routed as "devices-registry-admin",
// not "admin/devices" (reserved prefix) and not literally "/devices" (the
// real tenant-facing route).
//
// Settings accepts any key, including credentials (decision-log.md
// ADR-050 - by direct request, reversing ADR-048's original guard). This
// is a real departure from every other credential in this codebase, which
// stays local-only in *.secrets.json files (ADR-038): anything saved here
// is stored as plain text in tblDeviceRegistry and returned as plain text
// by ListDeviceRegistry to any caller holding a valid tenant x-api-key.
//
// No longer accepts/returns CapabilityIds - see DeviceRegistryDto and
// DeviceCapabilitiesAdminFunction (ADR-057). No DELETE route (ADR-058) -
// retire a device via PUT with Status: Retired instead, same shape
// MachinesFunction already established.
public class DeviceRegistryAdminFunction : ApiFunctionBase
{
    private readonly IDeviceService _deviceManagement;
    private readonly IDeviceConfigurationProjector _projector;

    public DeviceRegistryAdminFunction(
        IApiKeyAuthenticator authenticator,
        IDeviceService deviceManagement,
        IDeviceConfigurationProjector projector)
        : base(authenticator)
    {
        _deviceManagement = deviceManagement;
        _projector = projector;
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

        // Optional server-side filters (ADR-058) - "devices owned by this
        // agent" / "devices of this type," on top of the tenant/site
        // scoping ListAsync already does.
        var ownerAgentId = request.Query["ownerAgentId"].ToString();
        var deviceTypeId = request.Query["deviceTypeId"].ToString();

        var devices = await _deviceManagement.ListAsync(
            tenant,
            string.IsNullOrWhiteSpace(ownerAgentId) ? null : ownerAgentId,
            string.IsNullOrWhiteSpace(deviceTypeId) ? null : deviceTypeId,
            cancellationToken);

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

        var device = await _deviceManagement.CreateAsync(
            tenant,
            body.Name,
            body.DeviceTypeId,
            body.OwningAgentId,
            body.Location,
            body.Brand,
            body.Model,
            body.Firmware,
            body.RuntimeDeviceId,
            body.Settings,
            cancellationToken);

        if (device == null)
            return new BadRequestObjectResult($"OwningAgentId \"{body.OwningAgentId}\" doesn't exist for this tenant/site.");

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

        if (!Enum.TryParse<DeviceStatus>(body.Status, out _))
            return new BadRequestObjectResult("Status must be one of: Active, Disabled, Retired.");

        var device = await _deviceManagement.UpdateAsync(
            tenant,
            deviceId,
            body.Name,
            body.DeviceTypeId,
            body.OwningAgentId,
            body.Location,
            body.Brand,
            body.Model,
            body.Firmware,
            body.Status,
            body.RuntimeDeviceId,
            body.Settings,
            cancellationToken);

        if (device == null)
        {
            return new BadRequestObjectResult(
                $"DeviceId \"{deviceId}\" doesn't exist, or OwningAgentId \"{body.OwningAgentId}\" doesn't exist for this tenant/site.");
        }

        return new OkObjectResult(device);
    }

    // Read-only preview of the runtime device-config/*.json shape this
    // Device would project to (decision-log.md ADR-063) - nothing writes
    // anywhere, an admin diffs this against the real file by eye.
    [Function(nameof(GetProjectedConfig))]
    public async Task<IActionResult> GetProjectedConfig(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "devices-registry-admin/{deviceId}/projected-config")]
            HttpRequest request,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var projected = await _projector.ProjectAsync(tenant, deviceId, cancellationToken);

        if (projected == null)
            return new NotFoundResult();

        return new OkObjectResult(projected);
    }
}
