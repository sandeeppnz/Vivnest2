namespace Vivnest.Capabilities.Audio;

public sealed record AudioAlarmDetectedEvent(
    string DeviceId,
    DateTime DetectedAtUtc,
    double? FrequencyHz,
    int BurstCount,
    TimeSpan LongestTone);
