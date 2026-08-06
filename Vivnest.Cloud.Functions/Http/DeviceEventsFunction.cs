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

    [Function(nameof(GetEvents))]
    public async Task<IActionResult> GetEvents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "events")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        var take = ParseTake(request);

        var events = await _deviceQueryService.GetEventsAsync(
            tenant,
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

        // ?date=X switches to a single day, paginated with ?skip=N&take=N
        // (the gallery's "load more" within an expanded day); without it,
        // falls back to the flat ?take=N cap.
        if (TryParseDate(request, out var date))
        {
            var skip = ParseSkip(request);
            var take = ParseTake(request);

            var page = await _deviceQueryService.GetDeviceCapturesByDayAsync(
                tenant,
                deviceId,
                date,
                skip,
                take,
                cancellationToken);

            return new OkObjectResult(page);
        }

        var flatTake = ParseTake(request);

        var captures = await _deviceQueryService.GetDeviceCapturesAsync(
            tenant,
            deviceId,
            flatTake,
            cancellationToken);

        return new OkObjectResult(captures);
    }

    [Function(nameof(GetDeviceBattery))]
    public async Task<IActionResult> GetDeviceBattery(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "devices/{deviceId}/battery")]
            HttpRequest request,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        var take = ParseTake(request);

        var readings = await _deviceQueryService.GetDeviceBatteryReadingsAsync(
            tenant,
            deviceId,
            take,
            cancellationToken);

        return new OkObjectResult(readings);
    }

    [Function(nameof(GetDeviceCaptureDaySummaries))]
    public async Task<IActionResult> GetDeviceCaptureDaySummaries(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "devices/{deviceId}/captures/summary")]
            HttpRequest request,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        var days = TryParseDays(request, out var parsedDays) ? parsedDays : 30;

        var summaries = await _deviceQueryService.GetDeviceCaptureDaySummariesAsync(
            tenant,
            deviceId,
            days,
            cancellationToken);

        return new OkObjectResult(summaries);
    }

    private static bool TryParseDate(HttpRequest request, out DateOnly date)
    {
        date = default;

        return request.Query.TryGetValue("date", out var raw)
            && DateOnly.TryParse(raw, out date);
    }

    private static bool TryParseDays(HttpRequest request, out int days)
    {
        days = 0;

        return request.Query.TryGetValue("days", out var raw)
            && int.TryParse(raw, out days)
            && days > 0;
    }

    private static int ParseSkip(HttpRequest request)
    {
        return request.Query.TryGetValue("skip", out var raw)
            && int.TryParse(raw, out var skip)
            && skip > 0
            ? skip
            : 0;
    }
}
