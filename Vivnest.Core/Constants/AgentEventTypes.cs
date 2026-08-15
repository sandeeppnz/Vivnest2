namespace Vivnest.Core.Constants;

public static class AgentEventTypes
{
    public const string MetricsReported = "MetricsReported";

    // Audit trail for IAgentRuntimeConfigurationPublisher (decision-log.md
    // ADR-064) - fires whenever an Agent's "aiClassification" section is
    // actually written to agent-config/{agentId}.json.
    public const string ConfigPublished = "ConfigPublished";

    // Decision-log.md ADR-070 - see DeviceEventTypes.ConfigRolledBack, same
    // reasoning, mirrored here.
    public const string ConfigRolledBack = "ConfigRolledBack";
}
