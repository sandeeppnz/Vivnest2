using System.Text.Json.Serialization;
namespace Vivnest.Domain.Agents;

// Desired/Running software-version status (decision-log.md ADR-073) -
// computed at read time by AgentVersionStatusService, mirroring
// ConfigurationSyncStatus's own shape and reasoning: "Desired" is
// AgentInstallation.ImageVersion (admin-set, never re-derived here);
// "Running" is the latest AgentHeartbeat.FirmwareVersion (self-reported,
// baked into the image at build time - see build-and-push-agent.ps1's
// own -Version parameter).
//
// [JsonConverter] for the same reason ConfigurationSyncStatus needed
// it - no ambient JsonStringEnumConverter exists anywhere in this
// pipeline, so a real C# enum serialized without one would silently
// come back as a number instead of a name.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentVersionStatus
{
    // No AgentInstallation.ImageVersion is set - nothing to compare
    // against. Not the same as Unknown: there's no desired version at
    // all, not just an unreported running one.
    NeverDeployed,

    // A desired version is set, but no heartbeat has reported a
    // FirmwareVersion to compare it against yet.
    Unknown,

    // Desired and Running match exactly.
    UpToDate,

    // Both present, but differ - the Agent is running something other
    // than what's desired (an older version, a SHA-stamped build from
    // before ADR-073, or a version bump that hasn't been deployed yet).
    Outdated
}
