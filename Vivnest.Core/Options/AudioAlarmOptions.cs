namespace Vivnest.Core.Options;

/// <summary>
/// Per-device settings for the audio.alarm capability: listen to a short
/// clip from the device's audio track on a schedule and raise an alert when
/// it contains a fire-alarm-like tone. Null on <see cref="DeviceOptions"/>
/// for every device the capability is not assigned to - which is what keeps
/// the worker from sampling audio on cameras nobody asked it to.
/// <para>
/// A real alarm sounds until someone silences it, so sampling a clip every
/// <see cref="Interval"/> catches it with at most that much delay. The clip
/// is a sample of the stream, not a recording of it.
/// </para>
/// </summary>
public sealed class AudioAlarmOptions
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultClipDuration = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan MaxClipDuration = TimeSpan.FromSeconds(60);

    /// <summary>Gap between the starts of two clips.</summary>
    public TimeSpan Interval { get; init; }

    /// <summary>How much audio each clip captures.</summary>
    public TimeSpan ClipDuration { get; init; }

    /// <summary>
    /// After an alert, how long before the same device may alert again. The
    /// alarm is still ringing on the next clip; without this every clip
    /// would send another message.
    /// </summary>
    public TimeSpan Cooldown { get; init; } = DefaultCooldown;

    /// <summary>
    /// <see cref="Interval"/>, floored to something a loop can sleep on.
    /// Unset arrives as zero, and Task.Delay(TimeSpan.Zero) is a hot spin
    /// (see <see cref="DeviceOptions.EffectiveLivenessInterval"/>).
    /// </summary>
    public TimeSpan EffectiveInterval =>
        Interval > TimeSpan.Zero ? Interval : DefaultInterval;

    /// <summary>
    /// <see cref="ClipDuration"/>, defaulted when unset and capped so a
    /// typo cannot make ffmpeg hold the RTSP connection for minutes.
    /// </summary>
    public TimeSpan EffectiveClipDuration =>
        ClipDuration <= TimeSpan.Zero ? DefaultClipDuration
        : ClipDuration > MaxClipDuration ? MaxClipDuration
        : ClipDuration;

    public TimeSpan EffectiveCooldown =>
        Cooldown >= TimeSpan.Zero ? Cooldown : DefaultCooldown;
}
