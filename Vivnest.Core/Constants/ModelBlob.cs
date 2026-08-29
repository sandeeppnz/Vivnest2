namespace Vivnest.Core.Constants;

// Fixed convention, mirrors SharedConfigBlob/AgentConfigBlob (ADR-037):
// both Cloud (writing a version's file set at upload, ADR-124) and every
// High-type agent (downloading it into its local cache on first use)
// need to agree on where a model version's files live. Versions are
// immutable - a path under v{N} is written exactly once and never
// overwritten (ADR-069's rule, applied to artifacts).
public static class ModelBlob
{
    public const string ContainerName = "models";

    public static string BlobName(string modelId, int version, string fileName) =>
        $"{modelId}/v{version}/{fileName}";
}
