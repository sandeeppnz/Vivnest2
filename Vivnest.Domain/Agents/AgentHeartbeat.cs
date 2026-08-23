using Vivnest.Domain.Shared;

namespace Vivnest.Domain.Agents;

public class AgentHeartbeat : BaseIdentity
{
    public DateTime StartedUtc { get; set; }
    public DateTime LastHeartbeatUtc { get; set; }
    public string HostName { get; set; } = string.Empty;

    // The configured, human-friendly display name (Agent:Name) - distinct
    // from HostName (the OS machine name) and AgentId (the stable
    // identifier). Empty when never configured; consumers fall back to
    // AgentId in that case rather than showing a blank name.
    public string Name { get; set; } = string.Empty;

    public string FirmwareVersion { get; set; } = string.Empty;

    // Snapshot host facts, captured once at process start (RuntimeInformation) -
    // like FirmwareVersion, not a time-series metric, so these live on the
    // heartbeat, not AgentEvent.
    public string RuntimeVersion { get; set; } = string.Empty;
    public string OsDescription { get; set; } = string.Empty;

    public string? Error { get; set; }
    public TimeSpan HeartbeatInterval { get; set; }

    // Null when Home Assistant integration is disabled, or hasn't
    // connected even once yet - not the same as "was connected, now stale."
    public DateTime? HomeAssistantLastConnectedUtc { get; set; }

    // AgentConfigMetadataOptions.ConfigurationPublishedUtc (decision-log.md
    // ADR-065) - when this Agent's own agent-config/{agentId}.json was
    // last published by Admin; null if never published through that
    // pipeline. AgentHeartbeatWorker fires unconditionally every tick, so
    // this is always current, unlike DeviceHeartbeat's change-gated one.
    public DateTime? ConfigurationPublishedUtc { get; set; }

    // Decision-log.md ADR-068 - a coarse, Agent-level (not per-device)
    // signal: non-null when Program.cs's TryLoadRemoteDeviceConfigsAsync
    // caught an UnsupportedConfigurationSchemaException (or any other
    // per-device config load failure) for at least one owned device at
    // last startup. Joined message, not a list - this is "something
    // needs investigating," not a structured error report; the console
    // log at load time already has the per-blob detail. Null means no
    // load errors, not "never checked."
    public string? ConfigurationLoadError { get; set; }

    // AgentConfigMetadataOptions.ConfigurationVersion/ConfigurationHash
    // (decision-log.md ADR-069) - see DeviceHeartbeat's own copy of this
    // pair for the reasoning, mirrored here.
    public int? ConfigurationVersion { get; set; }
    public string? ConfigurationHash { get; set; }
}
