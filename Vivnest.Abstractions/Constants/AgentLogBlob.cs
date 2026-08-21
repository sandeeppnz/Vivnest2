namespace Vivnest.Abstractions.Constants;

// Fixed convention, mirroring AgentConfigBlob - both the Agent (uploading
// its own buffered log lines) and Cloud (generating a download URL for the
// dashboard) need to agree on where this lives.
public static class AgentLogBlob
{
    public const string ContainerName = "agent-logs";

    public static string BlobName(string agentId) => $"{agentId}.txt";
}
