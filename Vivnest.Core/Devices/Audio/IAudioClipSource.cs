using Vivnest.Core.Options;

namespace Vivnest.Core.Devices.Audio;

/// <summary>
/// Mono PCM, samples normalised to [-1, 1].
/// </summary>
public sealed record AudioClip(float[] Samples, int SampleRate)
{
    public TimeSpan Duration =>
        SampleRate > 0
            ? TimeSpan.FromSeconds((double)Samples.Length / SampleRate)
            : TimeSpan.Zero;
}

public interface IAudioClipSource
{
    Task<AudioClip> CaptureAsync(
        TimeSpan duration,
        CancellationToken cancellationToken = default);
}

public interface IAudioClipSourceFactory
{
    IAudioClipSource Create(DeviceOptions deviceOptions);
}
