namespace Vivnest.Core.Constants;

// Fixed convention, not configuration - both the Agent (downloading its own
// config on startup) and Cloud (uploading a new one from the dashboard)
// need to agree on where this lives, so it's a shared constant rather than
// a setting either side could drift on independently.
public static class AgentConfigBlob
{
    public const string ContainerName = "agent-config";

    public static string BlobName(string agentId) => $"{agentId}.json";
}
