using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Functions.Http;

// Admin > Capability dependency graph (decision-log.md ADR-062, Phase 5) -
// "ObjectDetection requires ImageCapture." Tenant x-api-key via
// ApiFunctionBase, same tier as CapabilitiesAdminFunction - the
// underlying data is global (like Capability itself), auth here is about
// who may call the admin API, not about the data being tenant-scoped.
// Add/Remove, not plain CRUD, since Add runs real validation (existence,
// self-reference, duplicate, circular dependency) - same POST-action
// shape DeviceCapabilitiesAdminFunction already uses for Assign/Unassign.
public class CapabilityDependenciesAdminFunction : ApiFunctionBase
{
    private readonly ICapabilityDependencyService _dependencies;

    public CapabilityDependenciesAdminFunction(
        IApiKeyAuthenticator authenticator,
        ICapabilityDependencyService dependencies)
        : base(authenticator)
    {
        _dependencies = dependencies;
    }

    [Function(nameof(ListDependencies))]
    public async Task<IActionResult> ListDependencies(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "capability-dependencies-admin")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var dependencies = await _dependencies.ListAllAsync(cancellationToken);

        return new OkObjectResult(dependencies);
    }

    [Function(nameof(AddDependency))]
    public async Task<IActionResult> AddDependency(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "capability-dependencies-admin/add")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        AddCapabilityDependencyRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<AddCapabilityDependencyRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.CapabilityId))
            return new BadRequestObjectResult("CapabilityId is required.");

        if (string.IsNullOrWhiteSpace(body.DependsOnCapabilityId))
            return new BadRequestObjectResult("DependsOnCapabilityId is required.");

        var result = await _dependencies.AddAsync(body.CapabilityId, body.DependsOnCapabilityId, cancellationToken);

        if (result.Error != null)
        {
            return result.Error switch
            {
                CapabilityDependencyError.CapabilityNotFound => new BadRequestObjectResult(result.ErrorMessage),
                CapabilityDependencyError.DependsOnCapabilityNotFound => new BadRequestObjectResult(result.ErrorMessage),
                CapabilityDependencyError.SelfReference => new BadRequestObjectResult(result.ErrorMessage),
                _ => new ConflictObjectResult(result.ErrorMessage)
            };
        }

        return new OkObjectResult(result.Dependency);
    }

    [Function(nameof(RemoveDependency))]
    public async Task<IActionResult> RemoveDependency(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "capability-dependencies-admin/{dependencyId}")]
            HttpRequest request,
        string dependencyId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (tenant.DevicesOnly)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        var removed = await _dependencies.RemoveAsync(dependencyId, cancellationToken);

        if (!removed)
            return new NotFoundResult();

        return new NoContentResult();
    }
}
