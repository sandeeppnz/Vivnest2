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
    private readonly IConfigurationSyncStatusService _syncStatus;
    private readonly IApiKeyManagementService _apiKeys;

    public AgentRegistryAdminFunction(
        IApiKeyAuthenticator authenticator,
        IAgentRegistryManagementService agentRegistryManagement,
        IAgentRuntimeConfigurationProjector projector,
        IAgentRuntimeConfigurationPublisher publisher,
        IConfigurationSyncStatusService syncStatus,
        IApiKeyManagementService apiKeys)
        : base(authenticator)
    {
        _agentRegistryManagement = agentRegistryManagement;
        _projector = projector;
        _publisher = publisher;
        _syncStatus = syncStatus;
        _apiKeys = apiKeys;
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

        var syncStatus = await _syncStatus.GetAgentStatusAsync(tenant, projected, cancellationToken);

        return new OkObjectResult(projected with { SyncStatus = syncStatus });
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

    // Issues this Agent its own scoped API key without re-registering it.
    //
    // Registration is the normal way an Agent gets a key, but it needs an
    // install token and drives a full container redeploy through the
    // Updater - far too heavy for an Agent that simply predates agent keys.
    // This is the migration path: call it, paste the returned key into that
    // Agent's appsettings.json as Agent:ApiKey, restart it, and its command
    // callbacks authenticate. Once every Agent is keyed, set
    // AgentAuth:RequireApiKey to true.
    //
    // The key is shown exactly once, like every other key in this system.
    // Re-issuing revokes the Agent's previous key, so this doubles as
    // rotation.
    [Function(nameof(IssueAgentKey))]
    public async Task<IActionResult> IssueAgentKey(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agents-registry-admin/{agentId}/issue-key")]
            HttpRequest request,
        string agentId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        // agentId here is the admin AgentId (this route family's identity
        // space), but the key has to be bound to the RuntimeAgentId, since
        // that is what the Agent sends and what the command routes compare
        // against. Resolved through the registry rather than assumed.
        var agents = await _agentRegistryManagement.ListAsync(tenant, cancellationToken);

        var agent = agents.FirstOrDefault(a =>
            string.Equals(a.AgentId.ToString(), agentId, StringComparison.OrdinalIgnoreCase));

        if (agent == null)
            return new NotFoundResult();

        if (string.IsNullOrWhiteSpace(agent.RuntimeAgentId))
        {
            return new BadRequestObjectResult(
                "This Agent has no RuntimeAgentId mapped yet, so a key would have nothing to bind to. "
                + "Set its RuntimeAgentId first, or register the Agent normally.");
        }

        var issued = await _apiKeys.IssueForExistingAgentAsync(
            tenant.TenantId, tenant.SiteId, agent.RuntimeAgentId, cancellationToken);

        if (issued == null)
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);

        return new OkObjectResult(new
        {
            agent.RuntimeAgentId,
            issued.KeyId,
            issued.ApiKey,
            issued.CreatedUtc,
            Note = "Set this as Agent:ApiKey in the Agent's appsettings.json and restart it. "
                 + "It will not be shown again."
        });
    }

    // Republishes an old immutable version's content as a brand-new
    // version, never mutating targetVersion's own blob (decision-log.md
    // ADR-070) - mirrors DeviceRegistryAdminFunction.RollbackDeviceConfig.
    [Function(nameof(RollbackAgentConfig))]
    public async Task<IActionResult> RollbackAgentConfig(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agents-registry-admin/{agentId}/rollback-config/{targetVersion:int}")]
            HttpRequest request,
        string agentId,
        int targetVersion,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var result = await _publisher.RollbackAsync(tenant, agentId, targetVersion, cancellationToken);

        if (result == null)
            return new NotFoundResult();

        return new OkObjectResult(result);
    }
}
