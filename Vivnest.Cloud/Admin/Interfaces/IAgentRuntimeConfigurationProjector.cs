using Vivnest.Cloud.Api.Dtos;
using Vivnest.Cloud.Auth;

namespace Vivnest.Cloud.Admin.Interfaces;

// Projects an Admin Agent into the shape its real agent-config/{agentId}.json
// blob's "aiClassification" section would have (decision-log.md ADR-064) -
// read-only, produces a preview a human diffs against the real file by
// eye. Does not write to Blob Storage. Queries every DeviceCapability
// across the tenant/site whose ExecutingAgentId is this Agent, runs each
// through the shared ICapabilityRuntimeProjector registry, and keeps only
// each result's AgentEntry - see IDeviceRuntimeConfigurationProjector for
// where DeviceEntry goes instead. Sibling to that projector, not a
// replacement for it - the two pipelines are independent (ADR-064).
public interface IAgentRuntimeConfigurationProjector
{
    // Returns null only if the Agent itself doesn't exist.
    Task<AgentRuntimeConfigurationDocumentDto?> ProjectAsync(
        TenantContext tenant,
        string agentId,
        CancellationToken cancellationToken = default);
}
