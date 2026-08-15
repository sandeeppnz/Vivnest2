namespace Vivnest.Core.Constants;

// Fixed convention, mirrors AgentConfigBlob (decision-log.md ADR-036) - both
// whatever authors a device's config (today: hand-edited, uploaded via az
// storage blob upload) and the Low-type agent that owns it need to
// agree on where it lives.
public static class DeviceConfigBlob
{
    public const string ContainerName = "device-config";

    public static string BlobName(string deviceId) => $"{deviceId}.json";

    // Decision-log.md ADR-069 - additive, alongside BlobName above, not a
    // replacement. VersionBlobName targets are written with failIfExists
    // (immutable); ManifestBlobName is overwritten freely (a mutable
    // pointer at the latest version). Blob Storage has no real
    // directories - the "/" is just a name convention - so this still
    // lives in the same flat "device-config" container ListBlobNamesAsync
    // already enumerates, distinguishable from a legacy BlobName(id)
    // entry purely by containing a "/".
    public static string VersionBlobName(string deviceId, int version) => $"{deviceId}/versions/{version}.json";

    public static string ManifestBlobName(string deviceId) => $"{deviceId}/current.json";
}
