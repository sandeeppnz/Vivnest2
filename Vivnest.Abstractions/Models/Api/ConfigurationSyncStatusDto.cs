using Vivnest.Core.Enums;

namespace Vivnest.Abstractions.Models.Api;

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
    ConfigurationSyncStatus Status,
    // Decision-log.md ADR-069 - null for an identity never republished
    // through the new versioned/manifest pipeline (still on the legacy
    // flat blob only, or the Agent build reporting AppliedVersion doesn't
    // exist yet) - PublishedUtc/AppliedUtc above still work in that case,
    // this is purely additive precision, not a replacement.
    int? PublishedVersion = null,
    int? AppliedVersion = null,
    string? ConfigurationHash = null);
