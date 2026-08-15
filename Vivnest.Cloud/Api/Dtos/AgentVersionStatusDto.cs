using Vivnest.Core.Enums;

namespace Vivnest.Cloud.Api.Dtos;

// Decision-log.md ADR-073 - mirrors ConfigurationSyncStatusDto's own
// shape. DesiredVersion is AgentInstallation.ImageVersion verbatim (null
// if never set); RunningVersion is the latest AgentHeartbeat.FirmwareVersion
// (null if no heartbeat has ever reported one).
public sealed record AgentVersionStatusDto(
    string? DesiredVersion,
    string? RunningVersion,
    AgentVersionStatus Status);
