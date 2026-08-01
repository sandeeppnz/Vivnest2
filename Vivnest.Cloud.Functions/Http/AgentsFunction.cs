using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Api;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Functions.Http;

public class AgentsFunction : ApiFunctionBase
{
    private readonly IAgentQueryService _agentQueryService;

    public AgentsFunction(
        IApiKeyAuthenticator authenticator,
        IAgentQueryService agentQueryService)
        : base(authenticator)
    {
        _agentQueryService = agentQueryService;
    }

    [Function(nameof(GetAgents))]
    public async Task<IActionResult> GetAgents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "agents")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        // Enforced server-side, not just hidden in the dashboard UI - a
        // devices-only key must not be able to see agent/system internals
        // even by calling this endpoint directly.
        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var agents = await _agentQueryService.GetAgentsAsync(
            tenant,
            cancellationToken);

        return new OkObjectResult(agents);
    }

    [Function(nameof(GetAgent))]
    public async Task<IActionResult> GetAgent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "agents/{agentId}")]
            HttpRequest request,
        string agentId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var agent = await _agentQueryService.GetAgentAsync(
            tenant,
            agentId,
            cancellationToken);

        if (agent == null)
            return new NotFoundResult();

        return new OkObjectResult(agent);
    }
}
