namespace Vivnest.Core.Constants;

public static class DeviceEventTypes
{
    public const string CameraCaptured = "CameraCaptured";

    public const string MotionDetected = "MotionDetected";

    public const string SmokeDetected = "SmokeDetected";

    public const string HumidityChanged = "HumidityChanged";

    public const string TemperatureChanged = "TemperatureChanged";

    public const string WaterLeakDetected = "WaterLeakDetected";

    public const string PowerReading = "PowerReading";

    public const string PowerStateChanged = "PowerStateChanged";

    public const string BatteryStatus = "BatteryStatus";

    public const string SinkCleanliness = "SinkCleanliness";

    public static string CameraCaptureFailed = "CameraCaptureFailed";

    public static string SmartPlugReadingFailed = "SmartPlugReadingFailed";

    public static string MotionSensorReadingFailed = "MotionSensorReadingFailed";

    // Fires on every capture ObjectDetection classifies, not just ones with
    // an unusual object - same "every classification, not just the
    // interesting case" shape SinkCleanliness already uses (ADR-034's
    // follow-up). Carries the full detection list (with boxes); a per-object
    // Unusual flag replaces what used to be a conditional, box-less event.
    public const string ObjectsDetected = "ObjectsDetected";

    // Audit trail for IDeviceRuntimeConfigurationPublisher (decision-log.md
    // ADR-064) - fires whenever a Device's runtime config is actually
    // written to device-config/*.json, distinct from every other event
    // type here (all real device telemetry, not an Admin action).
    public const string ConfigPublished = "ConfigPublished";

    // Decision-log.md ADR-070 - fires from IDeviceRuntimeConfigurationPublisher.RollbackAsync
    // instead of ConfigPublished, so the audit trail can tell a deliberate
    // rollback apart from a routine publish (spec section 22).
    public const string ConfigRolledBack = "ConfigRolledBack";

    // Decision-log.md ADR-077 (Phase 8 Pass 4) - see AgentEventTypes.
    // AgentOffline/AgentRecovered, same reasoning mirrored for Device:
    // persisted alongside the existing Telegram notification, not a
    // replacement for it.
    public const string DeviceOffline = "DeviceOffline";
    public const string DeviceRecovered = "DeviceRecovered";

    // No DeviceEventTypes.ConfigurationApplyFailed - ConfigurationLoadError
    // only ever lives on the owning Agent's own heartbeat, never per-device
    // (see ConfigurationSyncStatusService's own comment), so persisting it
    // fans out to every device that Agent owns for one real failure -
    // exactly the "redundant noise" EvaluateAndNotifyAsync's own
    // AgentCascade check already avoids for DeviceOffline. Persisted once,
    // on the Agent's own event history, via AgentEventTypes.ConfigurationApplyFailed
    // instead.
}
