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

        // ?days=N switches to a date-range timeline (every capture in the
        // window, grouped client-side by day); without it, falls back to
        // the flat ?take=N cap.
        if (TryParseDays(request, out var days))
        {
            var toUtc = DateTime.UtcNow;
            var fromUtc = toUtc.AddDays(-days);

            var timelineCaptures = await _deviceQueryService.GetDeviceCapturesByDateRangeAsync(
                tenant,
                deviceId,
                fromUtc,
                toUtc,
                cancellationToken);

            return new OkObjectResult(timelineCaptures);
        }

        var take = ParseTake(request);

        var captures = await _deviceQueryService.GetDeviceCapturesAsync(
            tenant,
            deviceId,
            take,
            cancellationToken);

        return new OkObjectResult(captures);
    }

    private static bool TryParseDays(HttpRequest request, out int days)
    {
        days = 0;

        return request.Query.TryGetValue("days", out var raw)
            && int.TryParse(raw, out days)
            && days > 0;
    }
}
