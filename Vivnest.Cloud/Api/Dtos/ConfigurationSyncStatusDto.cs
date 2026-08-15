using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Api.Dtos;

// Desired/Published/Applied status (decision-log.md ADR-068) - "Desired"
// is always just the live projected document this same response already
// carries, never persisted separately; PublishedUtc/AppliedUtc/ApplyError
// are the only two facts that need fetching (the currently published
// blob's own PublishedUtc, and the latest heartbeat's
// ConfigurationPublishedUtc/ConfigurationLoadError). Populated by
// ConfigurationSyncStatusService and attached to
// DeviceRuntimeConfigurationDocumentDto/AgentRuntimeConfigurationDocumentDto
// at the Function-handler layer, not inside either projector - computing
// it needs a blob download + a heartbeat lookup, neither of which the
// projectors themselves do today.
public sealed record ConfigurationSyncStatusDto(
    DateTime? PublishedUtc,
    DateTime? AppliedUtc,
    string? ApplyError,
    ConfigurationSyncStatus Status);
