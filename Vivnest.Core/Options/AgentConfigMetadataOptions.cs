namespace Vivnest.Core.Options;

/// <summary>
/// Root-bound (decision-log.md ADR-065, Phase 6C) - the one key
/// IAgentRuntimeConfigurationPublisher writes as a sibling to
/// "AiClassification" on agent-config/{agentId}.json, not nested under
/// any section, same top-level convention AiClassificationOptions/
/// HomeAssistantOptions already use for their own sections. Absent for
/// an agent-config blob never published through that pipeline.
/// </summary>
public sealed class AgentConfigMetadataOptions
{
    public DateTime? ConfigurationPublishedUtc { get; set; }
}
