using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Core.Enums;

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

    public AgentCommandsFunction(
        IApiKeyAuthenticator authenticator,
        IAgentCommandManagementService commands)
        : base(authenticator)
    {
        _commands = commands;
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
        var tenantId = request.Query["tenantId"].ToString();
        var siteId = request.Query["siteId"].ToString();

        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(siteId))
            return new BadRequestObjectResult("tenantId and siteId query parameters are required.");

        var command = await _commands.GetAsync(tenantId, siteId, commandId, cancellationToken);

        if (command == null || !string.Equals(command.TargetAgentId, agentId, StringComparison.Ordinal))
            return new NotFoundResult();

        return new OkObjectResult(command);
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

        if (body == null || string.IsNullOrWhiteSpace(body.TenantId) || string.IsNullOrWhiteSpace(body.SiteId))
            return new BadRequestObjectResult("TenantId and SiteId are required.");

        if (!Enum.TryParse<AgentCommandStatus>(body.Status, out var status))
            return new BadRequestObjectResult($"Unrecognized status '{body.Status}'.");

        var updated = await _commands.UpdateStatusAsync(
            body.TenantId,
            body.SiteId,
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
