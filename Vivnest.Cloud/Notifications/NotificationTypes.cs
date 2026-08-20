namespace Vivnest.Cloud.Notifications;

public static class NotificationTypes
{
    public const string DeviceOffline = "DeviceOffline";
    public const string DeviceRecovered = "DeviceRecovered";
    public const string AgentOffline = "AgentOffline";
    public const string AgentRecovered = "AgentRecovered";
    public const string CameraCaptured = "CameraCaptured";
    public const string SmartPlugPowerStateChanged = "SmartPlugPowerStateChanged";
    public const string MotionDetected = "MotionDetected";
    public const string SinkCleanliness = "SinkCleanliness";

    // Sprint 8 - an Error-level log line from the Agent process itself,
    // throttled per agent per error signature before it ever reaches here.
    public const string AgentErrorLogged = "AgentErrorLogged";

    // Decision-log.md ADR-077 (Phase 8 Pass 4) - a real config-load
    // failure on an Agent's own heartbeat, gated separately from
    // Online/Offline (see AgentHeartbeatEntity.LastNotifiedConfigurationLoadError)
    // so it fires once per distinct error, not once per health-check tick.
    public const string ConfigurationApplyFailed = "ConfigurationApplyFailed";
}
