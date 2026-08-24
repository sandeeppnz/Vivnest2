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

    // decision-log.md ADR-066 - checked by AgentHeartbeatWorker against
    // RuntimeConfigurationSchemaVersions.CurrentAgentSchemaVersion. Null
    // for a blob never published through this pipeline.
    public int? ConfigurationSchemaVersion { get; set; }

    // decision-log.md ADR-068 - NOT written by either publisher, unlike
    // the two fields above. Injected as a root-level
    // "ConfigurationLoadErrors" JSON array by
    // AgentConfigurationLoader.PublishStartupErrors, accumulated from
    // EVERY failure the whole configuration load hits (since 2026-08-24;
    // originally only UnsupportedConfigurationSchemaException on owned
    // devices): failed remote fetches, failed decrypts, a missing
    // encryption key against enc:v1: values, and whole-container device
    // load failures. Read once by AgentHeartbeatWorker to populate
    // AgentHeartbeat.ConfigurationLoadError.
    public IReadOnlyList<string>? ConfigurationLoadErrors { get; set; }

    // Decision-log.md ADR-069 - see DeviceOptions.ConfigurationVersion/
    // ConfigurationHash for the same reasoning, mirrored for the Agent's
    // own config.
    public int? ConfigurationVersion { get; set; }

    public string? ConfigurationHash { get; set; }

    // decision-log.md ADR-087 - the Admin registry's own Name
    // (AgentRegistryEntity.Name), replacing AgentOptions.Name (a locally
    // self-typed value with no connection to what Admin actually shows).
    // Null for a blob never published through this pipeline, same
    // tolerance as every other field here.
    public string? Name { get; set; }
}
