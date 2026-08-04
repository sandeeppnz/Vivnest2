using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Api;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;

namespace Vivnest.Cloud.Functions.Http;

public class AgentsFunction : ApiFunctionBase
{
    private readonly IAgentQueryService _agentQueryService;
    private readonly IAgentCommandPublisher _agentCommandPublisher;

    public AgentsFunction(
        IApiKeyAuthenticator authenticator,
        IAgentQueryService agentQueryService,
        IAgentCommandPublisher agentCommandPublisher)
        : base(authenticator)
    {
        _agentQueryService = agentQueryService;
        _agentCommandPublisher = agentCommandPublisher;
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

    [Function(nameof(GetAgentMetrics))]
    public async Task<IActionResult> GetAgentMetrics(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "agents/{agentId}/metrics")]
            HttpRequest request,
        string agentId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var days = TryParseDays(request, out var parsedDays) ? parsedDays : 30;

        var samples = await _agentQueryService.GetAgentMetricsAsync(
            tenant,
            agentId,
            days,
            cancellationToken);

        return new OkObjectResult(samples);
    }

    [Function(nameof(RestartAgent))]
    public async Task<IActionResult> RestartAgent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agents/{agentId}/restart")]
            HttpRequest request,
        string agentId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        // Same gating as every other /agents* route (ADR-012) - remotely
        // restarting a physical device is a bigger privilege than reading
        // its status, not a smaller one.
        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        // Tenant-scoped existence check before publishing - without this,
        // any valid tenant key could restart an agentId belonging to a
        // different tenant just by guessing/knowing its id (ADR-008: day-one
        // constraint, not a later migration).
        var agent = await _agentQueryService.GetAgentAsync(
            tenant,
            agentId,
            cancellationToken);

        if (agent == null)
            return new NotFoundResult();

        await _agentCommandPublisher.PublishRestartCommandAsync(
            agentId,
            cancellationToken);

        return new AcceptedResult();
    }

    private static bool TryParseDays(HttpRequest request, out int days)
    {
        days = 0;

        return request.Query.TryGetValue("days", out var raw)
            && int.TryParse(raw, out days)
            && days > 0;
    }
}
