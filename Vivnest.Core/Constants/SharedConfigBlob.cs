namespace Vivnest.Core.Constants;

// Fixed convention, mirrors AgentConfigBlob/DeviceConfigBlob
// (decision-log.md ADR-037) - both the Agent (downloading this blob on
// startup, both roles) and whoever authors it (today: hand-edited,
// uploaded via az storage blob upload) need to agree on where it lives.
// Unlike the other two, there's no per-entity ID - every agent, regardless
// of role or AgentId, loads the exact same single blob, so the name is a
// fixed const, not a method.
public static class SharedConfigBlob
{
    public const string ContainerName = "shared-config";
    public const string BlobName = "common-config.json";
}
