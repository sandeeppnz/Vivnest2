namespace Vivnest.Core.Constants;

// Fixed convention, mirrors AgentConfigBlob (decision-log.md ADR-036) - both
// whatever authors a device's config (today: hand-edited, uploaded via az
// storage blob upload) and the Capture-role agent that owns it need to
// agree on where it lives.
public static class DeviceConfigBlob
{
    public const string ContainerName = "device-config";

    public static string BlobName(string deviceId) => $"{deviceId}.json";
}
