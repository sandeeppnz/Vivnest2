using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Cloud.Options;
using Vivnest.Cloud.Interfaces;

namespace Vivnest.Cloud.Functions.Http;

// Decision-log.md ADR-079 - two distinct trust models on one Function
// class, same split this codebase already uses elsewhere (compare
// AgentsFunction's authenticated routes against AgentInstallationsFunction's
// RegisterInstallation/ReportDeployComplete). GetAgentCommands is
// dashboard-facing (tenant x-api-key, via ApiFunctionBase). GetCommand/
// UpdateCommandStatus are Agent-facing - the Agent has no tenant key,
// only the TenantId/SiteId it already knows from its own local config,
// passed explicitly and trusted directly (same model
// ReportDeployCompleteAsync's own callback already established).
public class AgentCommandsFunction : ApiFunctionBase
{
    private readonly IAgentCommandManagementService _commands;
    private readonly ICapabilityStore _capabilities;
    private readonly AgentAuthOptions _agentAuth;
    private readonly ILogger<AgentCommandsFunction> _logger;

    public AgentCommandsFunction(
        IApiKeyAuthenticator authenticator,
        IAgentCommandManagementService commands,
        ICapabilityStore capabilities,
        IOptions<AgentAuthOptions> agentAuth,
        ILogger<AgentCommandsFunction> logger)
        : base(authenticator)
    {
        _commands = commands;
        _capabilities = capabilities;
        _agentAuth = agentAuth.Value;
        _logger = logger;
    }

    // ADR-102 - translate the catalogue identity into the runtime one on
    // the way out to the Agent.
    //
    // The stored command row keeps CapabilityId as the catalogue GUID,
    // because a command row is admin history: it is what the dashboard
    // lists and what an audit reads, and it has to stay joinable to the
    // catalogue entry it came from. The Agent has never heard of that GUID
    // - its capabilities are named camera.capture / motion.sensor /
    // smartplug.monitor - so the wire carries CapabilityKey instead.
    //
    // Done here rather than in CommandDispatcher.ToDto because ToDto also
    // serves the dashboard's own command views, and this route is the only
    // Agent-facing one (see the class comment: GetCommand and
    // UpdateCommandStatus are the Agent's; GetAgentCommands is the
    // dashboard's).
    //
    // A value that resolves to no catalogue row is passed through
    // unchanged rather than blanked - a command the Agent cannot route is
    // better than a command it cannot even name in the failure it reports.
    private async Task<AgentCommandDto> ToRuntimeIdentityAsync(
        AgentCommandDto command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.CapabilityId))
            return command;

        var capability = await _capabilities.GetAsync(command.CapabilityId, cancellationToken);

        if (capability == null || string.IsNullOrWhiteSpace(capability.CapabilityKey))
        {
            _logger.LogWarning(
                "Command {CommandId} carries CapabilityId {CapabilityId}, which resolves to no " +
                "capability with a CapabilityKey; forwarding it unchanged.",
                command.CommandId,
                command.CapabilityId);

            return command;
        }

        return command with { CapabilityId = capability.CapabilityKey };
    }

    // Shared gate for the two Agent-facing routes. Returns the tenant/site
    // to operate on, or null to reject.
    //
    // An agent key (TenantContext.AgentId set) must name the same agent as
    // the route, and its own TenantId/SiteId are used - the caller-supplied
    // ones are ignored entirely, which is the whole point: those values
    // were previously trusted from the query string and request body.
    //
    // With AgentAuth:RequireApiKey false (the default), an unauthenticated
    // caller is still honoured on the caller-supplied ids, but logged, so
    // agents that predate agent keys keep working and are visible. A
    // *tenant* key is never accepted here even in grace mode - it is not
    // an agent, and accepting one would be a new hole rather than a
    // grandfathered one.
    private (string TenantId, string SiteId)? ResolveAgentScope(
        TenantContext? tenant, string agentId, string? claimedTenantId, string? claimedSiteId)
    {
        if (tenant?.AgentId is { Length: > 0 } keyAgentId)
        {
            return string.Equals(keyAgentId, agentId, StringComparison.Ordinal)
                ? (tenant.TenantId, tenant.SiteId)
                : null;
        }

        if (_agentAuth.RequireApiKey)
        {
            _logger.LogWarning(
                "Rejected an unauthenticated command callback for agent {AgentId}; AgentAuth:RequireApiKey is enabled.",
                agentId);

            return null;
        }

        if (string.IsNullOrWhiteSpace(claimedTenantId) || string.IsNullOrWhiteSpace(claimedSiteId))
            return null;

        _logger.LogWarning(
            "Agent {AgentId} called a command callback without an agent API key; honouring it because "
            + "AgentAuth:RequireApiKey is false. Re-register this agent (or set Agent:ApiKey) so it can authenticate.",
            agentId);

        return (claimedTenantId, claimedSiteId);
    }

    [Function(nameof(GetAgentCommands))]
    public async Task<IActionResult> GetAgentCommands(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "agents/{agentId}/commands")]
            HttpRequest request,
        string agentId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var commands = await _commands.GetByAgentAsync(
            tenant.TenantId, tenant.SiteId, agentId, cancellationToken);

        return new OkObjectResult(commands);
    }

    [Function(nameof(GetCommand))]
    public async Task<IActionResult> GetCommand(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "agents/{agentId}/commands/{commandId}")]
            HttpRequest request,
        string agentId,
        string commandId,
        CancellationToken cancellationToken)
    {
        var scope = ResolveAgentScope(
            await AuthenticateAgentAsync(request, cancellationToken),
            agentId,
            request.Query["tenantId"].ToString(),
            request.Query["siteId"].ToString());

        if (scope == null)
            return new UnauthorizedResult();

        var command = await _commands.GetAsync(scope.Value.TenantId, scope.Value.SiteId, commandId, cancellationToken);

        if (command == null || !string.Equals(command.TargetAgentId, agentId, StringComparison.Ordinal))
            return new NotFoundResult();

        return new OkObjectResult(
            await ToRuntimeIdentityAsync(command, cancellationToken));
    }

    [Function(nameof(UpdateCommandStatus))]
    public async Task<IActionResult> UpdateCommandStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "agents/{agentId}/commands/{commandId}/status")]
            HttpRequest request,
        string agentId,
        string commandId,
        CancellationToken cancellationToken)
    {
        AgentCommandStatusUpdateRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<AgentCommandStatusUpdateRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null)
            return new BadRequestObjectResult("A request body is required.");

        var scope = ResolveAgentScope(
            await AuthenticateAgentAsync(request, cancellationToken), agentId, body.TenantId, body.SiteId);

        if (scope == null)
            return new UnauthorizedResult();

        if (!Enum.TryParse<AgentCommandStatus>(body.Status, out var status))
            return new BadRequestObjectResult($"Unrecognized status '{body.Status}'.");

        var updated = await _commands.UpdateStatusAsync(
            scope.Value.TenantId,
            scope.Value.SiteId,
            commandId,
            status,
            body.Result,
            body.ErrorCode,
            body.ErrorMessage,
            cancellationToken);

        if (updated == null || !string.Equals(updated.TargetAgentId, agentId, StringComparison.Ordinal))
            return new NotFoundResult();

        return new OkObjectResult(updated);
    }
}
