namespace Vivnest.Core.Options;

public class TablesOptions
{
    public string AgentHeartbeat { get; set; } = "";
    public string DeviceHeartbeat { get; set; } = "";
    public string DeviceEvents { get; set; } = "";
    public string AgentEvents { get; set; } = "";
    public string DeviceSnapshotState { get; set; } = "";
    public string ApiKeys { get; set; } = "";
    public string Capabilities { get; set; } = "";
    public string AgentRegistry { get; set; } = "";
    public string DeviceTypes { get; set; } = "";
    public string DeviceRegistry { get; set; } = "";
    public string Tenants { get; set; } = "";
    public string Sites { get; set; } = "";
    public string Machines { get; set; } = "";
    public string AgentInstallations { get; set; } = "";

    // Decision-log.md ADR-071 - install tokens, hash-partitioned like
    // ApiKeys, deliberately its own table rather than fields on
    // AgentInstallations - see AgentInstallationTokenEntity for why
    // (global hash lookup with no tenant context, not partition-scoped).
    public string AgentInstallationTokens { get; set; } = "";
    public string DeviceCapabilities { get; set; } = "";
    public string AgentCapabilities { get; set; } = "";
    public string CapabilityDependencies { get; set; } = "";
    public string DeviceTypeCapabilities { get; set; } = "";

    // Decision-log.md ADR-069 - Published-state metadata for the new
    // immutable versioned configuration blob layout.
    public string DeviceConfiguration { get; set; } = "";
    public string AgentConfiguration { get; set; } = "";

    // Decision-log.md ADR-079 - Phase 9's command lifecycle table.
    public string AgentCommands { get; set; } = "";

    // Sprint 8 - per-agent, per-error-signature alert throttling state.
    public string AgentAlertState { get; set; } = "";
}
