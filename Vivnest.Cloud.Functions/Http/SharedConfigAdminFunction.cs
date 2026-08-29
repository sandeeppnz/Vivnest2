using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Vivnest.Cloud.Admin.Interfaces;

namespace Vivnest.Cloud.Functions.Http;

// AuthorizationLevel.Function (operator host key), same reasoning as
// TenantsFunction - the shared config blob is platform-wide, not
// tenant-scoped: it carries the (encrypted) storage connection string
// every agent in every tenant loads. A tenant x-api-key must never be
// able to rewrite it. Routed outside "admin/" like every other admin
// function (Azure reserves that prefix - see CapabilitiesAdminFunction).
public class SharedConfigAdminFunction
{
    private readonly ISharedConfigPublisher _publisher;

    public SharedConfigAdminFunction(ISharedConfigPublisher publisher)
    {
        _publisher = publisher;
    }

    // Shared-config self-publishing (ADR-120): regenerates
    // shared-config/common-config.json from the canonical in-code
    // defaults. Idempotent in effect - the document is deterministic
    // apart from the connection string's fresh AES-GCM nonce.
    [Function(nameof(PublishSharedConfig))]
    public async Task<IActionResult> PublishSharedConfig(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "shared-config-admin/publish")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _publisher.PublishAsync(cancellationToken);

        if (!result.Success)
            return new ConflictObjectResult(result.Error);

        return new OkObjectResult(result);
    }
}
