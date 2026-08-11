using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Admin;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;
using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Functions.Http;

// Admin > Machines CRUD (decision-log.md ADR-053). Tenant x-api-key via
// ApiFunctionBase, same tier as AgentRegistryAdminFunction/
// DeviceRegistryAdminFunction - a Machine is tenant-owned data, not the
// Tenant/Site ownership boundary itself (which uses the operator-only
// AuthorizationLevel.Function tier - see TenantsFunction/SitesFunction).
// Routed as "machines-admin" - same "avoid the reserved admin/ prefix"
// reasoning as every other admin route in this file's siblings.
public class MachinesFunction : ApiFunctionBase
{
    private readonly IMachineManagementService _machineManagement;

    public MachinesFunction(
        IApiKeyAuthenticator authenticator,
        IMachineManagementService machineManagement)
        : base(authenticator)
    {
        _machineManagement = machineManagement;
    }

    [Function(nameof(ListMachines))]
    public async Task<IActionResult> ListMachines(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "machines-admin")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var machines = await _machineManagement.ListAsync(tenant, cancellationToken);

        return new OkObjectResult(machines);
    }

    [Function(nameof(GetMachine))]
    public async Task<IActionResult> GetMachine(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "machines-admin/{machineId}")]
            HttpRequest request,
        string machineId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var machine = await _machineManagement.GetAsync(tenant, machineId, cancellationToken);

        if (machine == null)
            return new NotFoundResult();

        return new OkObjectResult(machine);
    }

    [Function(nameof(CreateMachine))]
    public async Task<IActionResult> CreateMachine(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "machines-admin")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        CreateMachineRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<CreateMachineRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.MachineId))
            return new BadRequestObjectResult("MachineId is required.");

        if (string.IsNullOrWhiteSpace(body.Name))
            return new BadRequestObjectResult("Name is required.");

        var machine = await _machineManagement.CreateAsync(
            tenant,
            body.MachineId,
            body.Name,
            body.Hostname,
            body.Description,
            body.OperatingSystem,
            body.Architecture,
            cancellationToken);

        if (machine == null)
            return new ConflictObjectResult($"Machine \"{body.MachineId}\" already exists.");

        return new OkObjectResult(machine);
    }

    [Function(nameof(UpdateMachine))]
    public async Task<IActionResult> UpdateMachine(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "machines-admin/{machineId}")]
            HttpRequest request,
        string machineId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        UpdateMachineRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<UpdateMachineRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.Name))
            return new BadRequestObjectResult("Name is required.");

        if (!Enum.TryParse<MachineStatus>(body.Status, out _))
            return new BadRequestObjectResult("Status must be one of: Active, Offline, Retired, Decommissioned.");

        var machine = await _machineManagement.UpdateAsync(
            tenant,
            machineId,
            body.Name,
            body.Hostname,
            body.Description,
            body.Status,
            body.OperatingSystem,
            body.Architecture,
            cancellationToken);

        if (machine == null)
            return new NotFoundResult();

        return new OkObjectResult(machine);
    }
}
