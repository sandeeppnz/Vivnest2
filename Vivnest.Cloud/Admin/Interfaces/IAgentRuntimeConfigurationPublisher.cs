using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin.Interfaces;

// Writes an Agent's projected "aiClassification" document into its real
// agent-config/{runtimeAgentId}.json blob (decision-log.md ADR-064) - the
// write-path counterpart to IAgentRuntimeConfigurationProjector's
// read-only preview. Hard-gated the same way the Device publisher is.
// Unlike the Device publisher, this never overwrites the whole blob - it
// replaces only the top-level "AiClassification" key, leaving every other
// section (e.g. a Low-type agent's "HomeAssistant") completely untouched,
// since Admin only ever owns that one section of this file.
public interface IAgentRuntimeConfigurationPublisher
{
    // Returns null only if the Agent itself doesn't exist.
    Task<AgentPublishResult?> PublishAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default);

    // Decision-log.md ADR-070 - see IDeviceRuntimeConfigurationPublisher.RollbackAsync,
    // same reasoning, mirrored here. Returns null only if the Agent itself
    // doesn't exist.
    Task<AgentPublishResult?> RollbackAsync(
        TenantContext tenant,
        string agentId,
        int targetVersion,
        CancellationToken cancellationToken = default);
}

public sealed record AgentPublishResult(
    bool Published,
    AgentRuntimeConfigurationDocumentDto Document,
    string? Reason);
