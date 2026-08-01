using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Api;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Functions.Http;

public class DevicesFunction : ApiFunctionBase
{
    private readonly IDeviceQueryService _deviceQueryService;

    public DevicesFunction(
        IApiKeyAuthenticator authenticator,
        IDeviceQueryService deviceQueryService)
        : base(authenticator)
    {
        _deviceQueryService = deviceQueryService;
    }

    [Function(nameof(GetDevices))]
    public async Task<IActionResult> GetDevices(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "devices")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        var devices = await _deviceQueryService.GetDevicesAsync(
            tenant,
            cancellationToken);

        return new OkObjectResult(devices);
    }

    [Function(nameof(GetDevice))]
    public async Task<IActionResult> GetDevice(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "devices/{deviceId}")]
            HttpRequest request,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        var device = await _deviceQueryService.GetDeviceAsync(
            tenant,
            deviceId,
            cancellationToken);

        if (device == null)
            return new NotFoundResult();

        return new OkObjectResult(device);
    }
}
