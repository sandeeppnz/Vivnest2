using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Api;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Functions.Http;

public class DeviceEventsFunction : ApiFunctionBase
{
    private readonly IDeviceQueryService _deviceQueryService;

    public DeviceEventsFunction(
        IApiKeyAuthenticator authenticator,
        IDeviceQueryService deviceQueryService)
        : base(authenticator)
    {
        _deviceQueryService = deviceQueryService;
    }

    [Function(nameof(GetDeviceEvents))]
    public async Task<IActionResult> GetDeviceEvents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "devices/{deviceId}/events")]
            HttpRequest request,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        var take = ParseTake(request);

        var events = await _deviceQueryService.GetDeviceEventsAsync(
            tenant,
            deviceId,
            take,
            cancellationToken);

        return new OkObjectResult(events);
    }

    [Function(nameof(GetDeviceCaptures))]
    public async Task<IActionResult> GetDeviceCaptures(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "devices/{deviceId}/captures")]
            HttpRequest request,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        var take = ParseTake(request);

        var captures = await _deviceQueryService.GetDeviceCapturesAsync(
            tenant,
            deviceId,
            take,
            cancellationToken);

        return new OkObjectResult(captures);
    }
}
