using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Functions.Http;

// Admin > Agent Capability declaration lifecycle (decision-log.md
// ADR-059). Tenant x-api-key via ApiFunctionBase, same tier as
// DeviceCapabilitiesAdminFunction. Assign/Unassign are POST actions (not
// a plain CRUD resource) since they're lifecycle operations with a real
// invariant (at most one active declaration per Agent+Capability pair),
// same shape DeviceCapabilitiesAdminFunction already established.
public class AgentCapabilitiesAdminFunction : ApiFunctionBase
{
    private readonly IAgentCapabilityAssignmentService _declarations;

    public AgentCapabilitiesAdminFunction(
        IApiKeyAuthenticator authenticator,
        IAgentCapabilityAssignmentService declarations)
        : base(authenticator)
    {
        _declarations = declarations;
    }

    [Function(nameof(ListDeclarationsByAgent))]
    public async Task<IActionResult> ListDeclarationsByAgent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "agent-capabilities-admin/by-agent/{agentId}")]
            HttpRequest request,
        string agentId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var declarations = await _declarations.ListByAgentAsync(tenant, agentId, cancellationToken);

        return new OkObjectResult(declarations);
    }

    [Function(nameof(AssignAgentCapability))]
    public async Task<IActionResult> AssignAgentCapability(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agent-capabilities-admin/assign")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        AssignAgentCapabilityRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<AssignAgentCapabilityRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.AgentId))
            return new BadRequestObjectResult("AgentId is required.");

        if (string.IsNullOrWhiteSpace(body.CapabilityId))
            return new BadRequestObjectResult("CapabilityId is required.");

        var declaration = await _declarations.AssignAsync(
            tenant, body.AgentId, body.CapabilityId, body.Settings, cancellationToken);

        if (declaration == null)
        {
            return new ConflictObjectResult(
                $"AgentId \"{body.AgentId}\" or CapabilityId \"{body.CapabilityId}\" doesn't exist, or this " +
                "agent already declares this capability - unassign it first.");
        }

        return new OkObjectResult(declaration);
    }

    [Function(nameof(UnassignAgentCapability))]
    public async Task<IActionResult> UnassignAgentCapability(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agent-capabilities-admin/unassign")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        UnassignAgentCapabilityRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<UnassignAgentCapabilityRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.AgentId))
            return new BadRequestObjectResult("AgentId is required.");

        if (string.IsNullOrWhiteSpace(body.CapabilityId))
            return new BadRequestObjectResult("CapabilityId is required.");

        var declaration = await _declarations.UnassignAsync(tenant, body.AgentId, body.CapabilityId, cancellationToken);

        if (declaration == null)
            return new NotFoundResult();

        return new OkObjectResult(declaration);
    }
}
