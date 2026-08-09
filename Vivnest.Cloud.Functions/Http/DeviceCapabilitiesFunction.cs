using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Api;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Functions.Http;

// Reads device-config/agent-config blobs directly, not Table Storage -
// the dashboard's Capabilities tab (decision-log.md ADR-040). Read-only -
// no PUT/POST here, matching this project's own history (ADR-025).
public class DeviceCapabilitiesFunction : ApiFunctionBase
{
    private readonly IDeviceCapabilitiesQueryService _capabilitiesQueryService;

    public DeviceCapabilitiesFunction(
        IApiKeyAuthenticator authenticator,
        IDeviceCapabilitiesQueryService capabilitiesQueryService)
        : base(authenticator)
    {
        _capabilitiesQueryService = capabilitiesQueryService;
    }

    [Function(nameof(GetDeviceCapabilities))]
    public async Task<IActionResult> GetDeviceCapabilities(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "devices/{deviceId}/capabilities")]
            HttpRequest request,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        var capabilities = await _capabilitiesQueryService.GetCapabilitiesAsync(
            tenant,
            deviceId,
            cancellationToken);

        if (capabilities == null)
            return new NotFoundResult();

        return new OkObjectResult(capabilities);
    }
}
