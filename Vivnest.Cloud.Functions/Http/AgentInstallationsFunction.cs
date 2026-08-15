using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Functions.Http;

// Admin > Agent Installations lifecycle (decision-log.md ADR-053). Tenant
// x-api-key via ApiFunctionBase, same tier as MachinesFunction/
// AgentRegistryAdminFunction. Install/Move/Uninstall are POST actions
// (not a plain CRUD resource) since they're lifecycle operations with
// real invariants (at most one active installation per Agent), not a bag
// of fields to overwrite.
public class AgentInstallationsFunction : ApiFunctionBase
{
    private readonly IAgentInstallationManagementService _installationManagement;
    private readonly IAgentVersionStatusService _versionStatus;

    public AgentInstallationsFunction(
        IApiKeyAuthenticator authenticator,
        IAgentInstallationManagementService installationManagement,
        IAgentVersionStatusService versionStatus)
        : base(authenticator)
    {
        _installationManagement = installationManagement;
        _versionStatus = versionStatus;
    }

    // Decision-log.md ADR-073 - attaches VersionStatus the same way
    // AgentRegistryAdminFunction attaches SyncStatus: computed at the
    // Function layer (not the management service), using the DTO's own
    // AgentId/ImageVersion the caller already has in hand.
    private async Task<AgentInstallationDto> WithVersionStatusAsync(
        TenantContext tenant, AgentInstallationDto installation, CancellationToken cancellationToken)
    {
        var status = await _versionStatus.GetStatusAsync(
            tenant, installation.AgentId, installation.ImageVersion, cancellationToken);

        return installation with { VersionStatus = status };
    }

    [Function(nameof(GetInstallationsByAgent))]
    public async Task<IActionResult> GetInstallationsByAgent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "agent-installations-admin/by-agent/{agentId}")]
            HttpRequest request,
        string agentId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var installations = await _installationManagement.GetByAgentAsync(tenant, agentId, cancellationToken);

        var withStatus = new List<AgentInstallationDto>(installations.Count);

        foreach (var installation in installations)
            withStatus.Add(await WithVersionStatusAsync(tenant, installation, cancellationToken));

        return new OkObjectResult(withStatus);
    }

    [Function(nameof(GetInstallationsByMachine))]
    public async Task<IActionResult> GetInstallationsByMachine(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "agent-installations-admin/by-machine/{machineId}")]
            HttpRequest request,
        string machineId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var installations = await _installationManagement.GetByMachineAsync(tenant, machineId, cancellationToken);

        var withStatus = new List<AgentInstallationDto>(installations.Count);

        foreach (var installation in installations)
            withStatus.Add(await WithVersionStatusAsync(tenant, installation, cancellationToken));

        return new OkObjectResult(withStatus);
    }

    [Function(nameof(GetActiveInstallationByAgent))]
    public async Task<IActionResult> GetActiveInstallationByAgent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "agent-installations-admin/active-by-agent/{agentId}")]
            HttpRequest request,
        string agentId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var installation = await _installationManagement.GetActiveByAgentAsync(tenant, agentId, cancellationToken);

        if (installation == null)
            return new NotFoundResult();

        return new OkObjectResult(await WithVersionStatusAsync(tenant, installation, cancellationToken));
    }

    [Function(nameof(GetActiveInstallationsByMachine))]
    public async Task<IActionResult> GetActiveInstallationsByMachine(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "agent-installations-admin/active-by-machine/{machineId}")]
            HttpRequest request,
        string machineId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var installations = await _installationManagement.GetActiveByMachineAsync(tenant, machineId, cancellationToken);

        var withStatus = new List<AgentInstallationDto>(installations.Count);

        foreach (var installation in installations)
            withStatus.Add(await WithVersionStatusAsync(tenant, installation, cancellationToken));

        return new OkObjectResult(withStatus);
    }

    [Function(nameof(InstallAgent))]
    public async Task<IActionResult> InstallAgent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agent-installations-admin/install")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        InstallAgentRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<InstallAgentRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.AgentId))
            return new BadRequestObjectResult("AgentId is required.");

        if (string.IsNullOrWhiteSpace(body.MachineId))
            return new BadRequestObjectResult("MachineId is required.");

        var installation = await _installationManagement.InstallAsync(
            tenant,
            body.AgentId,
            body.MachineId,
            body.ContainerId,
            body.ImageName,
            body.ImageVersion,
            cancellationToken);

        if (installation == null)
        {
            return new ConflictObjectResult(
                $"AgentId \"{body.AgentId}\" or MachineId \"{body.MachineId}\" doesn't exist, or Agent " +
                $"\"{body.AgentId}\" already has an active installation - use move or uninstall it first.");
        }

        return new OkObjectResult(installation);
    }

    [Function(nameof(MoveAgent))]
    public async Task<IActionResult> MoveAgent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agent-installations-admin/move")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        MoveAgentRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<MoveAgentRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.AgentId))
            return new BadRequestObjectResult("AgentId is required.");

        if (string.IsNullOrWhiteSpace(body.MachineId))
            return new BadRequestObjectResult("MachineId is required.");

        var installation = await _installationManagement.MoveAsync(
            tenant,
            body.AgentId,
            body.MachineId,
            body.ContainerId,
            body.ImageName,
            body.ImageVersion,
            cancellationToken);

        if (installation == null)
            return new BadRequestObjectResult($"AgentId \"{body.AgentId}\" or MachineId \"{body.MachineId}\" doesn't exist.");

        return new OkObjectResult(installation);
    }

    [Function(nameof(UninstallAgent))]
    public async Task<IActionResult> UninstallAgent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agent-installations-admin/uninstall")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        UninstallAgentRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<UninstallAgentRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.AgentId))
            return new BadRequestObjectResult("AgentId is required.");

        var installation = await _installationManagement.UninstallAsync(tenant, body.AgentId, cancellationToken);

        if (installation == null)
            return new NotFoundResult();

        return new OkObjectResult(installation);
    }

    // Decision-log.md ADR-072 - deliberately no AuthenticateAsync call,
    // unlike every other route in this file: the caller
    // (Vivnest.Agent.Updater, on a fresh Machine that has never talked to
    // Cloud before) has no tenant x-api-key to present yet. The install
    // token itself - a short-lived, single-use, hash-stored secret - is
    // the entire trust model here, the same reasoning
    // ApiKeyAuthenticator already established for tenant keys, just
    // narrower in scope (one installation, one use) and shorter-lived.
    [Function(nameof(RegisterInstallation))]
    public async Task<IActionResult> RegisterInstallation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agent-installations-admin/register")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        RegisterInstallationRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<RegisterInstallationRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.InstallToken))
            return new BadRequestObjectResult("InstallToken is required.");

        var result = await _installationManagement.RegisterAsync(body.InstallToken, cancellationToken);

        if (result == null)
            return new BadRequestObjectResult("Invalid, expired, or already-used install token.");

        return new OkObjectResult(result);
    }

    // Decision-log.md ADR-072 - same "no AuthenticateAsync" reasoning as
    // RegisterInstallation; by this point the Updater still has no tenant
    // key, only what RegisterInstallation's own response already handed
    // it back (TenantId/SiteId). Best-effort by design (see
    // IAgentInstallationManagementService.ReportDeployCompleteAsync's own
    // comment) - a missed or failed call here just means the installation
    // catches up to Active on the next real heartbeat instead.
    [Function(nameof(ReportDeployComplete))]
    public async Task<IActionResult> ReportDeployComplete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agent-installations-admin/{installationId}/deploy-complete")]
            HttpRequest request,
        string installationId,
        CancellationToken cancellationToken)
    {
        ReportDeployCompleteRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<ReportDeployCompleteRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.TenantId) || string.IsNullOrWhiteSpace(body.SiteId))
            return new BadRequestObjectResult("TenantId and SiteId are required.");

        var found = await _installationManagement.ReportDeployCompleteAsync(
            body.TenantId, body.SiteId, installationId, cancellationToken);

        if (!found)
            return new NotFoundResult();

        return new OkResult();
    }
}
