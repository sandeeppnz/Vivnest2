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
public class DeviceRegistryAdminFunction : ApiFunctionBase
{
    // Settings is for non-secret connection facts only (Host, Username,
    // RtspUsername, MACAddress, ChildDeviceId, ...) - real credentials stay
    // in local *.secrets.json files, never uploaded anywhere (ADR-038).
    // This is a best-effort guard against an obvious mistake, not a
    // security boundary - a key named "pwd" or "token" would slip past a
    // key-name check just as easily, so this only catches the literal,
    // most likely names.
    private static readonly string[] DisallowedSettingsKeys =
        ["password", "rtsppassword", "secret", "token", "accesstoken"];

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

        if (TryFindDisallowedSettingsKey(body.Settings, out var badKey))
            return new BadRequestObjectResult($"Settings must not contain credentials (key \"{badKey}\" looks like one) - use the device's local *.secrets.json file instead.");

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

        if (TryFindDisallowedSettingsKey(body.Settings, out var badKey))
            return new BadRequestObjectResult($"Settings must not contain credentials (key \"{badKey}\" looks like one) - use the device's local *.secrets.json file instead.");

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

    private static bool TryFindDisallowedSettingsKey(IReadOnlyDictionary<string, string>? settings, out string badKey)
    {
        badKey = "";

        if (settings == null)
            return false;

        foreach (var key in settings.Keys)
        {
            var normalized = key.Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();

            if (DisallowedSettingsKeys.Any(normalized.Contains))
            {
                badKey = key;
                return true;
            }
        }

        return false;
    }
}
