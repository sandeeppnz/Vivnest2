using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Admin;
using Vivnest.Cloud.Admin.Interfaces;
using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Functions.Http;

// Admin > Models - the model registry (ADR-124, see
// docs/architecture/model-registry-design.md). Tenant x-api-key,
// developer-gated, routed outside "admin/" - all per
// CapabilitiesAdminFunction's precedent.
//
// Version upload is multipart/form-data THROUGH this API (files land in
// memory, hashed server-side) - fine for today's models (~13 MB max,
// design caps the approach at ~25 MB/file); the write-SAS direct-upload
// path is the documented escape hatch when a model outgrows that.
public class ModelsAdminFunction : ApiFunctionBase
{
    private readonly IModelRegistryService _registry;

    public ModelsAdminFunction(
        IApiKeyAuthenticator authenticator,
        IModelRegistryService registry)
        : base(authenticator)
    {
        _registry = registry;
    }

    [Function(nameof(ListModels))]
    public async Task<IActionResult> ListModels(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "models-admin")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (!tenant.IsDeveloper)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        return new OkObjectResult(await _registry.ListAsync(tenant, cancellationToken));
    }

    [Function(nameof(CreateModel))]
    public async Task<IActionResult> CreateModel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "models-admin")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (!tenant.IsDeveloper)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        CreateModelRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<CreateModelRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.Name))
            return new BadRequestObjectResult("Name is required.");

        var model = await _registry.CreateAsync(
            tenant, body.Name.Trim(), body.Description, cancellationToken);

        return new OkObjectResult(model);
    }

    [Function(nameof(UpdateModel))]
    public async Task<IActionResult> UpdateModel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "models-admin/{modelId}")]
            HttpRequest request,
        string modelId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (!tenant.IsDeveloper)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        UpdateModelRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<UpdateModelRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || string.IsNullOrWhiteSpace(body.Name))
            return new BadRequestObjectResult("Name is required.");

        if (!Enum.TryParse<ModelStatus>(body.Status, out _))
            return new BadRequestObjectResult("Status must be one of: Active, Retired.");

        var model = await _registry.UpdateAsync(
            tenant, modelId, body.Name.Trim(), body.Description, body.Status, cancellationToken);

        if (model == null)
            return new NotFoundResult();

        return new OkObjectResult(model);
    }

    [Function(nameof(UploadModelVersion))]
    public async Task<IActionResult> UploadModelVersion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "models-admin/{modelId}/versions")]
            HttpRequest request,
        string modelId,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (!tenant.IsDeveloper)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        if (!request.HasFormContentType)
            return new BadRequestObjectResult("Expected multipart/form-data with one or more files.");

        var form = await request.ReadFormAsync(cancellationToken);
        var uploads = new List<ModelFileUpload>(form.Files.Count);

        foreach (var file in form.Files)
        {
            using var memory = new MemoryStream();
            await file.CopyToAsync(memory, cancellationToken);

            uploads.Add(new ModelFileUpload(Path.GetFileName(file.FileName), memory.ToArray()));
        }

        var result = await _registry.CreateVersionAsync(
            tenant, modelId, uploads, form["notes"].FirstOrDefault(), cancellationToken);

        if (result.Version == null)
            return new BadRequestObjectResult(result.Error);

        return new OkObjectResult(result.Version);
    }

    [Function(nameof(UpdateModelVersion))]
    public async Task<IActionResult> UpdateModelVersion(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "models-admin/{modelId}/versions/{version}")]
            HttpRequest request,
        string modelId,
        string version,
        CancellationToken cancellationToken)
    {
        var tenant = await AuthenticateAsync(request, cancellationToken);

        if (tenant == null)
            return new UnauthorizedResult();

        if (!tenant.IsDeveloper)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);

        if (!int.TryParse(version, NumberStyles.Integer, CultureInfo.InvariantCulture, out var versionNumber))
            return new BadRequestObjectResult("Version must be a whole number.");

        UpdateModelVersionRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<UpdateModelVersionRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Invalid JSON body.");
        }

        if (body == null || !Enum.TryParse<ModelVersionStatus>(body.Status, out _))
            return new BadRequestObjectResult("Status must be one of: Active, Retired.");

        var updated = await _registry.UpdateVersionAsync(
            tenant, modelId, versionNumber, body.Status, body.Notes, cancellationToken);

        if (updated == null)
            return new NotFoundResult();

        return new OkObjectResult(updated);
    }
}
