using System.Text.Json.Serialization;
using Vivnest.Domain.Agents;
using Vivnest.Domain.Capabilities;

namespace Vivnest.Domain.Shared;

// Desired/Published/Applied lifecycle status (decision-log.md ADR-068) -
// computed at read time by ConfigurationSyncStatusService, never
// persisted itself (only its inputs - the published blob's own
// PublishedUtc, and the latest heartbeat's ConfigurationPublishedUtc/
// ConfigurationLoadError - are).
//
// [JsonConverter] is load-bearing here, not decorative - every other
// "status" surfaced by this API is a plain string on its entity/DTO
// (DeviceRegistryEntity.Status, DeviceCapabilityStatus stored as
// .ToString(), etc.), not an actual C# enum serialized through
// System.Text.Json, so there's no ambient JsonStringEnumConverter
// configured anywhere in this pipeline. Without this, STJ's default
// numeric serialization would silently produce {"status":0} instead of
// {"status":"NeverPublished"} - caught during this ADR's own real-Azure
// verification pass, not by review.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConfigurationSyncStatus
{
    // No published blob exists yet for this identity.
    NeverPublished,

    // Published, but the latest heartbeat hasn't reported a matching
    // ConfigurationPublishedUtc yet - either it hasn't ticked since the
    // publish, or the Agent hasn't restarted to pick it up.
    Pending,

    // The latest heartbeat's ConfigurationPublishedUtc exactly matches
    // the published blob's.
    UpToDate,

    // The latest heartbeat reported a ConfigurationLoadError - the Agent
    // encountered the published configuration but could not apply it
    // (e.g. a SchemaVersion mismatch) and kept running on whatever it had
    // before.
    Failed,

    // Published, but no heartbeat has ever been recorded for this
    // identity - can't tell whether it's been applied.
    Unknown
}
