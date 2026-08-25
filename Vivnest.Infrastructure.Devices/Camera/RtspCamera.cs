using System.Diagnostics;
using System.Net.Sockets;
using Vivnest.Core.Devices.Camera;
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
                // Scrubbed, because ffmpeg echoes its input URL - which
                // carries the RTSP credentials - in its error output, and
                // this message travels a long way: it becomes
                // CameraCaptureResult.Error, then runtime.LastError, then
                // the heartbeat's Error column, which HealthMonitorService
                // appends to the DeviceOffline Telegram notification and
                // the dashboard shows verbatim. Without the scrub, one
                // failed capture could put the camera password in a chat
                // message and a persisted event payload.
                throw new InvalidOperationException(
                    $"FFmpeg snapshot failed for '{_options.DeviceId}' (exit code {process.ExitCode}): " +
                    ScrubCredentials(stderr, _options.Settings.RtspUsername, _options.Settings.RtspPassword));
            }

            var bytes = await File.ReadAllBytesAsync(
                tempFile,
                cancellationToken);

            return new MemoryStream(bytes);
        }
        finally
        {
            // Tolerant, because this finally runs on the timeout path too,
            // where ffmpeg was killed a moment ago and may still hold the
            // temp file's handle - a bare Delete threw IOException there
            // and REPLACED the TimeoutException that explained what
            // actually happened. A leaked file in the temp directory is a
            // far smaller problem than a masked diagnosis.
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

    // Removes the RTSP credentials from text ffmpeg produced. Both the
    // URL-escaped form (what we put in the URL, what ffmpeg echoes back)
    // and the raw form are replaced - redundant when the value has no
    // reserved characters, cheap insurance when it does. Empty values are
    // skipped: replacing "" would be a no-op loop hazard, and a device
    // with no credentials has nothing to leak.
    public static string ScrubCredentials(string text, string username, string password)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        foreach (var secret in new[]
        {
            Uri.EscapeDataString(password ?? ""),
            password,
            Uri.EscapeDataString(username ?? ""),
            username,
        })
        {
            if (!string.IsNullOrEmpty(secret))
                text = text.Replace(secret, "***", StringComparison.Ordinal);
        }

        return text;
    }

    // Kill() can race a process that's already exiting on its own -
    // harmless, just means the timeout and the natural exit crossed paths.
    // Win32Exception (access denied, already terminating) is tolerated for
    // the same reason: this method runs on the way to throwing a
    // TimeoutException, and a kill hiccup must not replace that diagnosis.
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
        catch (System.ComponentModel.Win32Exception)
        {
            // Already terminating, or access denied - either way the
            // timeout is the story, not the kill.
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


