using System.Text.Json.Serialization;

namespace Vivnest.Core.Enums;

// Decision-log.md ADR-076 - [JsonConverter] added because MachineDto.
// OperationalStatus is the first place this enum is ever serialized
// directly (every other consumer - DeviceSummaryDto.Status,
// AgentSummaryDto.Status - stores it as entity.Status.ToString(), a plain
// string, never the enum itself) - without this, System.Text.Json falls
// back to numeric serialization, same gap AgentVersionStatus/
// ConfigurationSyncStatus already hit and fixed the same way.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeviceHeartbeatStatus
{
    Online,
    Offline,
    Error,
    Unknown,
    Warning,

    // Decision-log.md ADR-076 - the Admin lifecycle (Disabled/Retired for
    // a Device, Inactive for an Agent) has taken this entity out of
    // service; "is it currently reachable" no longer means anything for
    // it. Deliberately distinct from Offline/Unknown, both of which mean
    // "should be running but isn't/can't tell" - NotApplicable means
    // "not expected to be running at all."
    NotApplicable
}


