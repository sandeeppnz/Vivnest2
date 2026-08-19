namespace Vivnest.Core.Constants;

// Fixed convention, mirrors AgentConfigBlob (decision-log.md ADR-036) - both
// whatever authors a device's config (today: hand-edited, uploaded via az
// storage blob upload) and the Low-type agent that owns it need to
// agree on where it lives.
//
// Every name is now built from a ConfigBlobKey, which carries the tenant
// and site as a path prefix. A key with either missing produces exactly
// the old unscoped name, so the two layouts are the same code path rather
// than a branch at every call site - see ConfigBlobKey for why the scoping
// exists and what it does and does not fix.
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
    // lives in the same flat "device-config" container ListBlobNamesAsync
    // already enumerates, distinguishable from a legacy BlobName(id)
    // entry purely by containing a "/".
    public static string VersionBlobName(ConfigBlobKey key, int version) =>
        $"{key.Prefix}{key.RuntimeId}/versions/{version}.json";

    public static string ManifestBlobName(ConfigBlobKey key) =>
        $"{key.Prefix}{key.RuntimeId}/current.json";

    // Unscoped overloads - the pre-existing layout. Kept because published
    // blobs still live there, Agents that predate scoping still read there,
    // and the publishers write both during the transition.
    public static string BlobName(string deviceId) => BlobName(ConfigBlobKey.Unscoped(deviceId));

    public static string VersionBlobName(string deviceId, int version) =>
        VersionBlobName(ConfigBlobKey.Unscoped(deviceId), version);

    public static string ManifestBlobName(string deviceId) =>
        ManifestBlobName(ConfigBlobKey.Unscoped(deviceId));
}
