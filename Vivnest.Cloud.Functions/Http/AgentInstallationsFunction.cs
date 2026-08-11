using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Admin;
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

    public AgentInstallationsFunction(
        IApiKeyAuthenticator authenticator,
        IAgentInstallationManagementService installationManagement)
        : base(authenticator)
    {
        _installationManagement = installationManagement;
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

        return new OkObjectResult(installations);
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

        return new OkObjectResult(installations);
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

        return new OkObjectResult(installation);
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

        return new OkObjectResult(installations);
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
}
