namespace Vivnest.Abstractions.Constants;

// Fixed convention, mirrors AgentConfigBlob (decision-log.md ADR-036) - both
// whatever authors a device's config (today: hand-edited, uploaded via az
// storage blob upload) and the Low-type agent that owns it need to
// agree on where it lives.
//
// Every name is built from a ConfigBlobKey, which carries the tenant and
// site as a path prefix - see ConfigBlobKey for why the scoping exists and
// what it does and does not fix.
public static class DeviceConfigBlob
{
    public const string ContainerName = "device-config";

    // Everything under one tenant/site. Used by the Agent to enumerate only
    // its own configs instead of the whole container.
    public static string Prefix(string tenantId, string siteId) =>
        new ConfigBlobKey(tenantId, siteId, "").Prefix;

    public static string BlobName(ConfigBlobKey key) =>
        $"{key.Prefix}{key.RuntimeId}.json";

    // Decision-log.md ADR-069 - additive, alongside BlobName above, not a
    // replacement. VersionBlobName targets are written with failIfExists
    // (immutable); ManifestBlobName is overwritten freely (a mutable
    // pointer at the latest version). Blob Storage has no real
    // directories - the "/" is just a name convention - so this still
    // lives in the same "device-config" container ListBlobNamesAsync
    // already enumerates, distinguishable from the BlobName(key) entry
    // beside it purely by containing a further "/".
    public static string VersionBlobName(ConfigBlobKey key, int version) =>
        $"{key.Prefix}{key.RuntimeId}/versions/{version}.json";

    public static string ManifestBlobName(ConfigBlobKey key) =>
        $"{key.Prefix}{key.RuntimeId}/current.json";
}
