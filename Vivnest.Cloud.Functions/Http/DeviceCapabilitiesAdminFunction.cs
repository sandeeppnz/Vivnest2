using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Admin;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Functions.Http;

// Admin > Device Capability assignment lifecycle (decision-log.md
// ADR-057). Tenant x-api-key via ApiFunctionBase, same tier as
// MachinesFunction/AgentInstallationsFunction. Assign/Unassign are POST
// actions (not a plain CRUD resource) since they're lifecycle operations
// with a real invariant (at most one active assignment per Device+
// Capability pair), same shape AgentInstallationsFunction already
// established for Install/Move/Uninstall.
public class DeviceCapabilitiesAdminFunction : ApiFunctionBase
{
    private readonly ICapabilityAssignmentService _assignments;

    public DeviceCapabilitiesAdminFunction(
        IApiKeyAuthenticator authenticator,
        ICapabilityAssignmentService assignments)
        : base(authenticator)
    {
        _assignments = assignments;
    }

    [Function(nameof(ListAssignmentsByDevice))]
    public async Task<IActionResult> ListAssignmentsByDevice(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "device-capabilities-admin/by-device/{deviceId}")]
            HttpRequest request,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var assignments = await _assignments.ListByDeviceAsync(tenant, deviceId, cancellationToken);

        return new OkObjectResult(assignments);
    }

    [Function(nameof(AssignCapability))]
    public async Task<IActionResult> AssignCapability(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "device-capabilities-admin/assign")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        AssignCapabilityRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<AssignCapabilityRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.DeviceId))
            return new BadRequestObjectResult("DeviceId is required.");

        if (string.IsNullOrWhiteSpace(body.CapabilityId))
            return new BadRequestObjectResult("CapabilityId is required.");

        var assignment = await _assignments.AssignAsync(
            tenant,
            body.DeviceId,
            body.CapabilityId,
            body.ExecutingAgentId,
            body.Enabled,
            body.Settings,
            cancellationToken);

        if (assignment == null)
        {
            return new ConflictObjectResult(
                $"DeviceId \"{body.DeviceId}\" or CapabilityId \"{body.CapabilityId}\" doesn't exist, " +
                $"ExecutingAgentId \"{body.ExecutingAgentId}\" doesn't exist for this tenant/site, or this " +
                "device already has an active assignment for this capability - update or unassign it first.");
        }

        return new OkObjectResult(assignment);
    }

    [Function(nameof(UpdateAssignment))]
    public async Task<IActionResult> UpdateAssignment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "device-capabilities-admin/{deviceCapabilityId}")]
            HttpRequest request,
        string deviceCapabilityId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        UpdateCapabilityAssignmentRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<UpdateCapabilityAssignmentRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null)
            return new BadRequestObjectResult("Invalid JSON body.");

        var assignment = await _assignments.UpdateAssignmentAsync(
            tenant,
            deviceCapabilityId,
            body.ExecutingAgentId,
            body.Enabled,
            body.Settings,
            cancellationToken);

        if (assignment == null)
            return new NotFoundResult();

        return new OkObjectResult(assignment);
    }

    [Function(nameof(UnassignCapability))]
    public async Task<IActionResult> UnassignCapability(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "device-capabilities-admin/unassign")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        UnassignCapabilityRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<UnassignCapabilityRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.DeviceId))
            return new BadRequestObjectResult("DeviceId is required.");

        if (string.IsNullOrWhiteSpace(body.CapabilityId))
            return new BadRequestObjectResult("CapabilityId is required.");

        var assignment = await _assignments.UnassignAsync(tenant, body.DeviceId, body.CapabilityId, cancellationToken);

        if (assignment == null)
            return new NotFoundResult();

        return new OkObjectResult(assignment);
    }
}
