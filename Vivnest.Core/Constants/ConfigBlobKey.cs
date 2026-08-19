namespace Vivnest.Core.Constants;

// Identifies one entity's configuration blobs within its container.
//
// Until now the name was just the runtime id, so every tenant's device and
// agent configuration sat side by side in one flat container. The Agent
// startup path listed the WHOLE container and downloaded every blob to
// find the handful it owns, which meant routinely pulling other tenants'
// device names, locations, brands and RTSP URLs across the wire - the
// credential fields are ciphertext (ADR-085), the rest is not.
//
// Scoping the name by tenant and site fixes the normal path: an Agent
// enumerates and downloads only its own prefix. It does NOT by itself stop
// a determined Agent reading another tenant's blobs, because Agents still
// hold an account-level Storage:ConnectionString - that is a separate,
// larger finding. What this does is make per-prefix SAS possible at all,
// and stop the routine cross-tenant reads happening by accident.
//
// TenantId/SiteId being empty is meaningful, not an error: it selects the
// old unscoped layout, which is what every already-published blob uses and
// what an Agent that predates this change still reads.
public readonly record struct ConfigBlobKey(string TenantId, string SiteId, string RuntimeId)
{
    // True when this key addresses the old flat layout. Reads fall back to
    // it; writes still populate it during the transition.
    public bool IsUnscoped =>
        string.IsNullOrWhiteSpace(TenantId) || string.IsNullOrWhiteSpace(SiteId);

    // "" for an unscoped key, so Prefix + name composes correctly in both
    // layouts and callers need no branch of their own.
    public string Prefix => IsUnscoped ? "" : $"{TenantId}/{SiteId}/";

    public ConfigBlobKey Unscoped() => new("", "", RuntimeId);

    public static ConfigBlobKey Unscoped(string runtimeId) => new("", "", runtimeId);
}
