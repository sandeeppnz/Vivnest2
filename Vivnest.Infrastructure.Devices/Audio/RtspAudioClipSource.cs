using System.Diagnostics;
using Vivnest.Core.Devices.Audio;
using Vivnest.Core.Options;
using Vivnest.Infrastructure.Camera;

namespace Vivnest.Infrastructure.Audio;

// Pulls a short audio clip off the same RTSP stream the camera's snapshots
// come from. Same ffmpeg conventions as RtspCamera: ArgumentList rather
// than a command string, a hard timeout (a stalled stream otherwise hangs
// WaitForExitAsync forever), a tolerant temp-file delete, and credentials
// scrubbed from anything ffmpeg says before it travels anywhere.
public sealed class RtspAudioClipSource : IAudioClipSource
{
    private const int DefaultRtspPort = 554;
    private const string DefaultStreamPath = "stream1";

    // 16 kHz mono is plenty for a 2.5-4 kHz alarm tone (Nyquist is 8 kHz)
    // and keeps a 10 s clip at ~320 KB.
    internal const int SampleRate = 16000;

    // Added to the clip length: connecting and negotiating the stream takes
    // a few seconds before any audio flows.
    private static readonly TimeSpan ConnectAllowance = TimeSpan.FromSeconds(20);

    private readonly DeviceOptions _options;

    public RtspAudioClipSource(DeviceOptions options)
    {
        _options = options;
    }

    public async Task<AudioClip> CaptureAsync(
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var tempFile = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid()}.pcm");

        var rtspUrl =
            $"rtsp://{Uri.EscapeDataString(_options.Settings.RtspUsername)}:{Uri.EscapeDataString(_options.Settings.RtspPassword)}" +
            $"@{_options.Settings.Host}:{DefaultRtspPort}/{DefaultStreamPath}";

        var timeout = duration + ConnectAllowance;

        using var process = new Process();

        process.StartInfo.FileName = RtspCamera.ResolveFfmpegPath();

        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-rtsp_transport");
        process.StartInfo.ArgumentList.Add("tcp");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(rtspUrl);
        process.StartInfo.ArgumentList.Add("-vn");
        process.StartInfo.ArgumentList.Add("-t");
        process.StartInfo.ArgumentList.Add(
            duration.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("-ac");
        process.StartInfo.ArgumentList.Add("1");
        process.StartInfo.ArgumentList.Add("-ar");
        process.StartInfo.ArgumentList.Add(SampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("s16le");
        process.StartInfo.ArgumentList.Add(tempFile);

        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardError = true;

        try
        {
            process.Start();

            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
                when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                RtspCamera.TryKill(process);

                throw new TimeoutException(
                    $"FFmpeg audio clip for '{_options.DeviceId}' did not exit within {timeout} - killed.");
            }

            var stderr = await stderrTask;

            if (process.ExitCode != 0 || !File.Exists(tempFile))
            {
                throw new InvalidOperationException(
                    $"FFmpeg audio clip failed for '{_options.DeviceId}' (exit code {process.ExitCode}): " +
                    RtspCamera.ScrubCredentials(stderr, _options.Settings.RtspUsername, _options.Settings.RtspPassword));
            }

            var bytes = await File.ReadAllBytesAsync(tempFile, cancellationToken);

            // ffmpeg exits 0 with an empty file when the stream has no
            // audio track at all. Reported as what it is, rather than
            // handing the detector silence, which would read as "no alarm"
            // forever on a device that can never hear one.
            if (bytes.Length < 2)
            {
                throw new InvalidOperationException(
                    $"'{_options.DeviceId}' produced no audio - the stream has no audio track, or the camera's microphone is switched off.");
            }

            return new AudioClip(DecodePcm16(bytes), SampleRate);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    // Little-endian signed 16-bit, which is what -f s16le writes regardless
    // of the host. A trailing odd byte (a truncated write) is dropped.
    public static float[] DecodePcm16(byte[] bytes)
    {
        var samples = new float[bytes.Length / 2];

        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)(bytes[2 * i] | (bytes[2 * i + 1] << 8));
            samples[i] = value / 32768f;
        }

        return samples;
    }
}
