

namespace Vivnest.Domain.Agents;

// Decision-log.md ADR-071 - models the provisioning lifecycle only, not
// live health (Online/Offline stays exactly as it is today: computed live
// from heartbeat staleness by HealthMonitorService, never a stored status
// here). Pending -> Installing -> Installed -> Active is the happy path;
// Updating is a re-entry from Active when a new deploy is issued, landing
// back on Installed once that deploy is confirmed. Decommissioned is
// terminal (renamed from the original Removed - same meaning, matches the
// Phase 7 spec's own vocabulary; no migration story needed since this is
// early-stage dev/demo data, not a live production table).
public enum AgentInstallationStatus
{
    // Created, an install token issued, nothing has registered yet.
    Pending,

    // A fresh RuntimeAgentId was just assigned via the registration
    // endpoint (or the target already had one, for a Move) and a deploy
    // command has been enqueued - the container may not exist yet.
    Installing,

    // The Updater reported a successful docker deploy - the container
    // exists and started, but no heartbeat has proven it's actually
    // running yet.
    Installed,

    // A new deploy was issued against an already-Active installation
    // (e.g. a version bump) - reverts to Installed once that deploy is
    // confirmed, then Active again on the next real heartbeat.
    Updating,

    // At least one real heartbeat has been received from this
    // installation's Agent - the strongest signal available that it's
    // genuinely running, not just that a deploy command was issued.
    Active,

    Decommissioned
}
