using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Constants;

namespace Vivnest.Cloud.Functions.Http;

public class AgentsFunction : ApiFunctionBase
{
    // Short-lived, same reasoning as DeviceQueryService's image SAS URLs -
    // this is generated fresh on every request, not cached, so there's no
    // benefit to a longer window.
    private static readonly TimeSpan LogsUrlValidFor = TimeSpan.FromMinutes(15);

    // Decision-log.md ADR-079 - no per-user identity exists in this
    // codebase yet (ADR-012: "permissions are a plain bool until a second
    // dimension is real") - RequestedBy is a fixed placeholder rather
    // than a fabricated user system, until one exists.
    private const string DashboardRequestedBy = "Dashboard";

    // Command audit: history says WHO issued a command, via the key's
    // admin-given name (roles-on-keys, 2026-08-29). Unnamed and legacy
    // keys keep the old generic label.
    private static string RequestedBy(Vivnest.Cloud.Auth.TenantContext tenant) =>
        string.IsNullOrWhiteSpace(tenant.KeyName) ? DashboardRequestedBy : tenant.KeyName;

    private readonly IAgentQueryService _agentQueryService;
    private readonly IAgentCommandPublisher _agentCommandPublisher;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IBlobStorageService _blobStorage;
    private readonly IAgentInstallationManagementService _installationManagement;
    private readonly IAgentPublishAllService _publishAll;

    public AgentsFunction(
        IApiKeyAuthenticator authenticator,
        IAgentQueryService agentQueryService,
        IAgentCommandPublisher agentCommandPublisher,
        ICommandDispatcher commandDispatcher,
        IBlobStorageService blobStorage,
        IAgentInstallationManagementService installationManagement,
        IAgentPublishAllService publishAll)
        : base(authenticator)
    {
        _agentQueryService = agentQueryService;
        _agentCommandPublisher = agentCommandPublisher;
        _commandDispatcher = commandDispatcher;
        _blobStorage = blobStorage;
        _installationManagement = installationManagement;
        _publishAll = publishAll;
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
        if (!tenant.IsDeveloper)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        // Decision-log.md ADR-079 - RestartAgent now goes through
        // ICommandDispatcher (persist + enqueue, tracked in
        // tblAgentCommands) instead of calling IAgentCommandPublisher
        // directly - the dispatcher's own tenant-scoped ownership check
        // (same reasoning ADR-008 already established: without it, any
        // valid tenant key could restart an agentId belonging to a
        // different tenant just by guessing/knowing its id) replaces the
        // explicit GetAgentAsync call this route used to make itself.
        var command = await _commandDispatcher.DispatchAsync(
            tenant,
            AgentCommandTypes.RestartAgent,
            agentId,
            RequestedBy(tenant),
            cancellationToken: cancellationToken);

        if (command == null)
            return new NotFoundResult();

        return new AcceptedResult(location: null!, value: command);
    }

    // "Publish all & refresh" (ADR-121): publish every Active owned
    // device, publish the agent config, queue one refresh - the whole
    // make-the-agent-match sequence as a single idempotent action.
    [Function(nameof(PublishAllAndRefresh))]
    public async Task<IActionResult> PublishAllAndRefresh(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agents/{agentId}/publish-all")]
            HttpRequest request,
        string agentId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (!tenant.IsDeveloper)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var report = await _publishAll.PublishAllAsync(
            tenant, agentId, RequestedBy(tenant), cancellationToken);

        if (report == null)
            return new NotFoundResult();

        return new OkObjectResult(report);
    }

    [Function(nameof(RefreshConfiguration))]
    public async Task<IActionResult> RefreshConfiguration(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agents/{agentId}/refresh-config")]
            HttpRequest request,
        string agentId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (!tenant.IsDeveloper)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        // Decision-log.md ADR-080 - same dispatcher entry point as
        // RestartAgent; CommandDispatcher itself resolves what "the
        // latest published version" is at dispatch time, no payload
        // needed from the caller.
        var command = await _commandDispatcher.DispatchAsync(
            tenant,
            AgentCommandTypes.RefreshConfiguration,
            agentId,
            RequestedBy(tenant),
            cancellationToken: cancellationToken);

        if (command == null)
            return new NotFoundResult();

        return new AcceptedResult(location: null!, value: command);
    }

    [Function(nameof(ApplyConfiguration))]
    public async Task<IActionResult> ApplyConfiguration(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agents/{agentId}/apply-config")]
            HttpRequest request,
        string agentId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (!tenant.IsDeveloper)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        ApplyConfigurationRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<ApplyConfigurationRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null)
            return new BadRequestObjectResult("ConfigurationVersion is required.");

        // Decision-log.md ADR-080 - the caller's raw request body becomes
        // the command's Payload as dispatched; CommandDispatcher validates
        // the requested version exists and normalizes the payload before
        // persisting (scoped to the Agent's own config this pass - no
        // TargetDeviceId support yet).
        var command = await _commandDispatcher.DispatchAsync(
            tenant,
            AgentCommandTypes.ApplyConfiguration,
            agentId,
            RequestedBy(tenant),
            payload: JsonSerializer.Serialize(body),
            cancellationToken: cancellationToken);

        if (command == null)
            return new NotFoundResult();

        return new AcceptedResult(location: null!, value: command);
    }

    [Function(nameof(ExecuteCapability))]
    public async Task<IActionResult> ExecuteCapability(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agents/{agentId}/execute-capability")]
            HttpRequest request,
        string agentId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        ExecuteCapabilityRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<ExecuteCapabilityRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.TargetDeviceId) || string.IsNullOrWhiteSpace(body.CapabilityId))
            return new BadRequestObjectResult("TargetDeviceId and CapabilityId are required.");

        // Decision-log.md ADR-081 - CommandDispatcher's ExecuteCapability
        // validation branch (written in Pass 1, exercised for the first
        // time here) does the full Built-in/Derived authorization chain -
        // this route just passes the caller's Device/Capability through.
        var command = await _commandDispatcher.DispatchAsync(
            tenant,
            AgentCommandTypes.ExecuteCapability,
            agentId,
            RequestedBy(tenant),
            targetDeviceId: body.TargetDeviceId,
            capabilityId: body.CapabilityId,
            cancellationToken: cancellationToken);

        if (command == null)
            return new NotFoundResult();

        return new AcceptedResult(location: null!, value: command);
    }

    [Function(nameof(DeployAgent))]
    public async Task<IActionResult> DeployAgent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agents/{agentId}/deploy")]
            HttpRequest request,
        string agentId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        // Same tenant-scoped gating as RestartAgent, a deliberate choice
        // rather than an oversight - ADR-024 flagged Deploy as a bigger
        // privilege than Restart (arbitrary container replacement, not a
        // temporary monitoring gap) and worth stricter gating "when it's
        // built." Reusing today's tier anyway for v1: there is exactly one
        // tenant in practice today, so a separate auth tier has no real
        // consumer yet - revisit if this ever becomes genuinely
        // multi-tenant. See ADR-028.
        if (!tenant.IsDeveloper)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var agent = await _agentQueryService.GetAgentAsync(
            tenant,
            agentId,
            cancellationToken);

        if (agent == null)
            return new NotFoundResult();

        // Decision-log.md ADR-073 - agentId here is the RuntimeAgentId
        // (this route's own identity space, matching AgentQueryService's
        // heartbeat-keyed lookup above), not the admin AgentId
        // AgentInstallation is actually keyed by - resolved via the same
        // reverse lookup RegisterAsync uses internally. Null (no Agent
        // found, no active installation, or no ImageVersion set) falls
        // back to :latest, unchanged from before this resolution existed.
        var imageVersion = await _installationManagement.GetActiveImageVersionByRuntimeAgentIdAsync(
            tenant, agentId, cancellationToken);

        await _agentCommandPublisher.PublishDeployCommandAsync(
            agentId,
            imageVersion,
            cancellationToken);

        return new AcceptedResult();
    }

    [Function(nameof(GetAgentLogs))]
    public async Task<IActionResult> GetAgentLogs(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "agents/{agentId}/logs")]
            HttpRequest request,
        string agentId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (!tenant.IsDeveloper)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        // Tenant-scoped existence check, same reasoning as RestartAgent -
        // without it, any valid tenant key could read another tenant's
        // agent logs just by guessing/knowing its id.
        var agent = await _agentQueryService.GetAgentAsync(
            tenant,
            agentId,
            cancellationToken);

        if (agent == null)
            return new NotFoundResult();

        var url = _blobStorage.GenerateReadSasUri(
            AgentLogBlob.ContainerName,
            AgentLogBlob.BlobName(agentId),
            LogsUrlValidFor);

        return new OkObjectResult(new AgentLogsDto(url.ToString()));
    }

    private static bool TryParseDays(HttpRequest request, out int days)
    {
        days = 0;

        return request.Query.TryGetValue("days", out var raw)
            && int.TryParse(raw, out days)
            && days > 0;
    }
}
