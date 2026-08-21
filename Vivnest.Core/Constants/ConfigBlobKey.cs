namespace Vivnest.Core.Constants;

// Identifies one entity's configuration blobs within its container.
//
// The name is scoped by tenant and site. Before ADR-091 it was just the
// runtime id, so every tenant's device and agent configuration sat side by
// side in one flat container, and the Agent startup path listed the WHOLE
// container and downloaded every blob to find the handful it owns - which
// meant routinely pulling other tenants' device names, locations, brands
// and RTSP URLs across the wire. The credential fields are ciphertext
// (ADR-085); the rest was not.
//
// Scoping fixes the normal path: an Agent enumerates and downloads only its
// own prefix. It does NOT by itself stop a determined Agent reading another
// tenant's blobs, because Agents still hold an account-level
// Storage:ConnectionString - a separate, larger finding. What it does is
// make per-prefix SAS possible at all, and stop the routine cross-tenant
// reads happening by accident.
//
// The unscoped layout, its read fallbacks and its backfill were removed on
// 2026-08-21 along with the blobs themselves - see ADR-091. An empty tenant
// or site is now an error rather than a second layout: it used to silently
// select the flat names, and with nothing left there it would silently find
// no configuration at all.
public readonly record struct ConfigBlobKey
{
    public ConfigBlobKey(string tenantId, string siteId, string runtimeId)
    {
        // RuntimeId is deliberately not checked - Prefix(tenantId, siteId)
        // builds a container prefix with no entity in it, and passes "".
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(siteId))
        {
            throw new ArgumentException(
                $"A config blob key needs both a tenant and a site (got tenant '{tenantId}', site '{siteId}'). " +
                "An Agent missing Agent:TenantId or Agent:SiteId cannot address its own configuration.");
        }

        TenantId = tenantId;
        SiteId = siteId;
        RuntimeId = runtimeId;
    }

    public string TenantId { get; }

    public string SiteId { get; }

    public string RuntimeId { get; }

    public string Prefix => $"{TenantId}/{SiteId}/";
}
