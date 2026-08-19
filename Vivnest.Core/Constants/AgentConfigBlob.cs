namespace Vivnest.Core.Constants;

// Fixed convention, not configuration - both the Agent (downloading its own
// config on startup) and Cloud (uploading a new one from the dashboard)
// need to agree on where this lives, so it's a shared constant rather than
// a setting either side could drift on independently.
//
// Tenant/site scoped through ConfigBlobKey, exactly as DeviceConfigBlob is -
// see ConfigBlobKey for the reasoning. An empty tenant or site yields the
// old unscoped name.
public static class AgentConfigBlob
{
    public const string ContainerName = "agent-config";

    public static string Prefix(string tenantId, string siteId) =>
        new ConfigBlobKey(tenantId, siteId, "").Prefix;

    public static string BlobName(ConfigBlobKey key) =>
        $"{key.Prefix}{key.RuntimeId}.json";

    // See DeviceConfigBlob.VersionBlobName/ManifestBlobName (decision-log.md
    // ADR-069) - same reasoning, mirrored for the Agent side.
    public static string VersionBlobName(ConfigBlobKey key, int version) =>
        $"{key.Prefix}{key.RuntimeId}/versions/{version}.json";

    public static string ManifestBlobName(ConfigBlobKey key) =>
        $"{key.Prefix}{key.RuntimeId}/current.json";

    // Unscoped overloads - see DeviceConfigBlob's own note.
    public static string BlobName(string agentId) => BlobName(ConfigBlobKey.Unscoped(agentId));

    public static string VersionBlobName(string agentId, int version) =>
        VersionBlobName(ConfigBlobKey.Unscoped(agentId), version);

    public static string ManifestBlobName(string agentId) =>
        ManifestBlobName(ConfigBlobKey.Unscoped(agentId));
}
