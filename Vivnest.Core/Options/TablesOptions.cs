namespace Vivnest.Core.Options;

// Every name defaults to its canonical value (ADR-120): the table names
// are platform constants, not deployment choices - no environment has
// ever renamed one, but a missing entry in hand-assembled config crashed
// the Agent on CreateIfNotExists("") during the 2026-08-29 rebuild.
// Configuration can still override any of them; absence now means the
// canonical name instead of an invalid empty string. These defaults are
// also what SharedConfigPublisher serializes into the published
// common-config.json, so there is exactly one place the names live.
public class TablesOptions
{
    public string AgentHeartbeat { get; set; } = "tblAgentHeartbeat";
    public string DeviceHeartbeat { get; set; } = "tblDeviceHeartbeat";
    public string DeviceEvents { get; set; } = "tblDeviceEvents";
    public string AgentEvents { get; set; } = "tblAgentEvents";
    public string DeviceSnapshotState { get; set; } = "tblDeviceSnapshotState";
    public string ApiKeys { get; set; } = "tblApiKeys";
    public string Capabilities { get; set; } = "tblCapabilities";
    public string AgentRegistry { get; set; } = "tblAgentRegistry";
    public string DeviceTypes { get; set; } = "tblDeviceTypes";
    public string DeviceRegistry { get; set; } = "tblDeviceRegistry";
    public string Tenants { get; set; } = "tblTenants";
    public string Sites { get; set; } = "tblSites";
    public string Machines { get; set; } = "tblMachines";
    public string AgentInstallations { get; set; } = "tblAgentInstallations";

    // Decision-log.md ADR-071 - install tokens, hash-partitioned like
    // ApiKeys, deliberately its own table rather than fields on
    // AgentInstallations - see AgentInstallationTokenEntity for why
    // (global hash lookup with no tenant context, not partition-scoped).
    public string AgentInstallationTokens { get; set; } = "tblAgentInstallationTokens";
    public string DeviceCapabilities { get; set; } = "tblDeviceCapabilities";
    public string AgentCapabilities { get; set; } = "tblAgentCapabilities";
    public string CapabilityDependencies { get; set; } = "tblCapabilityDependencies";
    public string DeviceTypeCapabilities { get; set; } = "tblDeviceTypeCapabilities";

    // Decision-log.md ADR-069 - Published-state metadata for the new
    // immutable versioned configuration blob layout.
    public string DeviceConfiguration { get; set; } = "tblDeviceConfiguration";
    public string AgentConfiguration { get; set; } = "tblAgentConfiguration";

    // Decision-log.md ADR-079 - Phase 9's command lifecycle table.
    public string AgentCommands { get; set; } = "tblAgentCommands";

    // Sprint 8 - per-agent, per-error-signature alert throttling state.
    public string AgentAlertState { get; set; } = "tblAgentAlertState";

    // ADR-124 - the model registry catalogue and its immutable versions.
    public string Models { get; set; } = "tblModels";
    public string ModelVersions { get; set; } = "tblModelVersions";
}
