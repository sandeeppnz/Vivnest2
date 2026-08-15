using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Functions.Http;

// Admin > Agents pre-registration CRUD (decision-log.md ADR-043). Tenant
// x-api-key via ApiFunctionBase, same as CapabilitiesAdminFunction - not
// AgentsFunction's read-only precedent, since this is a write API.
// Routed as "agents-registry-admin", not "admin/agents" - avoids both
// the reserved Azure Functions "admin/" prefix (see CapabilitiesAdminFunction)
// and any collision with the real tenant-facing "/agents*" routes.
public class AgentRegistryAdminFunction : ApiFunctionBase
{
    private readonly IAgentRegistryManagementService _agentRegistryManagement;
    private readonly IAgentRuntimeConfigurationProjector _projector;
    private readonly IAgentRuntimeConfigurationPublisher _publisher;

    public AgentRegistryAdminFunction(
        IApiKeyAuthenticator authenticator,
        IAgentRegistryManagementService agentRegistryManagement,
        IAgentRuntimeConfigurationProjector projector,
        IAgentRuntimeConfigurationPublisher publisher)
        : base(authenticator)
    {
        _agentRegistryManagement = agentRegistryManagement;
        _projector = projector;
        _publisher = publisher;
    }

    [Function(nameof(ListAgentRegistry))]
    public async Task<IActionResult> ListAgentRegistry(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "agents-registry-admin")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var agents = await _agentRegistryManagement.ListAsync(tenant, cancellationToken);

        return new OkObjectResult(agents);
    }

    [Function(nameof(CreateAgentRegistry))]
    public async Task<IActionResult> CreateAgentRegistry(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agents-registry-admin")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        CreateAgentRegistryRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<CreateAgentRegistryRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.Name))
            return new BadRequestObjectResult("Name is required.");

        if (!Enum.TryParse<AgentType>(body.Type, out _))
            return new BadRequestObjectResult("Type must be one of: Low, High.");

        var agent = await _agentRegistryManagement.CreateAsync(
            tenant,
            body.Name,
            body.Description,
            body.FirmwareVersion,
            body.Type,
            body.RuntimeAgentId,
            cancellationToken);

        return new OkObjectResult(agent);
    }

    [Function(nameof(UpdateAgentRegistry))]
    public async Task<IActionResult> UpdateAgentRegistry(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "agents-registry-admin/{agentId}")]
            HttpRequest request,
        string agentId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        UpdateAgentRegistryRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<UpdateAgentRegistryRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.Name))
            return new BadRequestObjectResult("Name is required.");

        if (!Enum.TryParse<AgentType>(body.Type, out _))
            return new BadRequestObjectResult("Type must be one of: Low, High.");

        if (!Enum.TryParse<AgentStatus>(body.Status, out _))
            return new BadRequestObjectResult("Status must be one of: Active, Inactive.");

        var agent = await _agentRegistryManagement.UpdateAsync(
            tenant,
            agentId,
            body.Name,
            body.Description,
            body.Status,
            body.FirmwareVersion,
            body.Type,
            body.RuntimeAgentId,
            cancellationToken);

        if (agent == null)
            return new NotFoundResult();

        return new OkObjectResult(agent);
    }

    [Function(nameof(DeleteAgentRegistry))]
    public async Task<IActionResult> DeleteAgentRegistry(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "agents-registry-admin/{agentId}")]
            HttpRequest request,
        string agentId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var deleted = await _agentRegistryManagement.DeleteAsync(tenant, agentId, cancellationToken);

        if (!deleted)
            return new NotFoundResult();

        return new NoContentResult();
    }

    // Read-only preview of the runtime agent-config/{agentId}.json
    // "AiClassification" section this Agent would project to
    // (decision-log.md ADR-064) - nothing writes anywhere, mirrors
    // DeviceRegistryAdminFunction.GetAgentProjectedConfig.
    [Function(nameof(GetAgentProjectedConfig))]
    public async Task<IActionResult> GetAgentProjectedConfig(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "agents-registry-admin/{agentId}/projected-config")]
            HttpRequest request,
        string agentId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var projected = await _projector.ProjectAsync(tenant, agentId, cancellationToken);

        if (projected == null)
            return new NotFoundResult();

        return new OkObjectResult(projected);
    }

    // Writes the projected "AiClassification" section into this Agent's
    // real agent-config/{runtimeAgentId}.json blob (decision-log.md
    // ADR-064) - mirrors DeviceRegistryAdminFunction.PublishAgentConfig.
    [Function(nameof(PublishAgentConfig))]
    public async Task<IActionResult> PublishAgentConfig(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agents-registry-admin/{agentId}/publish-config")]
            HttpRequest request,
        string agentId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var result = await _publisher.PublishAsync(tenant, agentId, cancellationToken);

        if (result == null)
            return new NotFoundResult();

        return new OkObjectResult(result);
    }
}
