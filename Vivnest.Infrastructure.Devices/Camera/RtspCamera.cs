using System.Diagnostics;
using System.Net.Sockets;
using Vivnest.Core.Camera;
using Vivnest.Core.Options;

namespace Vivnest.Infrastructure.Camera;

public class RtspCamera : ICamera
{
    private const int DefaultRtspPort = 554;
    private const string DefaultStreamPath = "stream1";
    private static readonly TimeSpan ReachabilityTimeout = TimeSpan.FromSeconds(5);

    // ffmpeg grabbing one frame over a local RTSP connection normally takes
    // a few seconds; 20s is a generous upper bound, not a tuned value.
    // Without this, a stalled RTSP stream (camera hiccup, network blip that
    // doesn't cleanly close the TCP connection) hangs WaitForExitAsync
    // forever - no exception, no LastError, no LastActivityUtc update -
    // silently freezing this device's entire capture loop for the rest of
    // the process's life while every other device on the same agent keeps
    // working fine. Confirmed live: camera-001 stuck reporting Unknown for
    // 17+ hours with a perfectly healthy agent process underneath it.
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(20);

    private readonly DeviceOptions _options;

    public RtspCamera(DeviceOptions options)
    {
        _options = options;
    }

    public async Task<bool> IsReachableAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            using var timeoutCts = new CancellationTokenSource(ReachabilityTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

            await client.ConnectAsync(
                _options.Settings.Host,
                DefaultRtspPort,
                linkedCts.Token);

            return client.Connected;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<Stream> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var tempFile = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid()}.jpg");

        var rtspUrl =
            $"rtsp://{Uri.EscapeDataString(_options.Settings.RtspUsername)}:{Uri.EscapeDataString(_options.Settings.RtspPassword)}" +
            $"@{_options.Settings.Host}:{DefaultRtspPort}/{DefaultStreamPath}";

        using var process = new Process();

        process.StartInfo.FileName = ResolveFfmpegPath();

        // ArgumentList lets the runtime apply correct Win32 argument
        // quoting per element, so a credential containing a quote or
        // space can't break out of its argument and inject extra flags.
        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-rtsp_transport");
        process.StartInfo.ArgumentList.Add("tcp");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(rtspUrl);
        process.StartInfo.ArgumentList.Add("-frames:v");
        process.StartInfo.ArgumentList.Add("1");
        process.StartInfo.ArgumentList.Add(tempFile);

        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardError = true;

        try
        {
            process.Start();

            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeoutCts = new CancellationTokenSource(CaptureTimeout);
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
                TryKill(process);

                throw new TimeoutException(
                    $"FFmpeg snapshot for '{_options.DeviceId}' did not exit within {CaptureTimeout} - killed.");
            }

            var stderr = await stderrTask;

            if (process.ExitCode != 0 || !File.Exists(tempFile))
            {
                throw new InvalidOperationException(
                    $"FFmpeg snapshot failed for '{_options.DeviceId}' (exit code {process.ExitCode}): {stderr}");
            }

            var bytes = await File.ReadAllBytesAsync(
                tempFile,
                cancellationToken);

            return new MemoryStream(bytes);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    // Kill() can race a process that's already exiting on its own -
    // harmless, just means the timeout and the natural exit crossed paths.
    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

    // Windows dev machines use the bundled Tools/ffmpeg.exe; Linux (the
    // container image) installs ffmpeg via apt-get, so it's resolved off
    // PATH instead - no bundled binary to bundle or find.
    private static string ResolveFfmpegPath()
    {
        if (!OperatingSystem.IsWindows())
            return "ffmpeg";

        var bundledPath = Path.Combine(
            AppContext.BaseDirectory,
            "Tools",
            "ffmpeg.exe");

        if (!File.Exists(bundledPath))
        {
            throw new FileNotFoundException(
                $"FFmpeg not found at '{bundledPath}'");
        }

        return bundledPath;
    }
}


