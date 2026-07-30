using System.Diagnostics;
using Vivnest.Core.Camera;
using Vivnest.Core.Options;

namespace Vivnest.Infrastructure.Camera;

public class RtspCamera : ICamera
{
    private readonly DeviceOptions _options;

    public RtspCamera(DeviceOptions options)
    {
        _options = options;
    }

    public async Task<Stream> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var tempFile = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid()}.jpg");

        var rtspUrl =
            $"rtsp://{Uri.EscapeDataString(_options.Settings.RtspUsername)}:{Uri.EscapeDataString(_options.Settings.RtspPassword)}" +
            $"@{_options.Settings.Host}:554/stream1";

        var ffmpegPath = Path.Combine(
            AppContext.BaseDirectory,
            "Tools",
            "ffmpeg.exe");

        if (!File.Exists(ffmpegPath))
        {
            throw new FileNotFoundException(
                $"FFmpeg not found at '{ffmpegPath}'");
        }

        var process = new Process();

        process.StartInfo.FileName = ffmpegPath;

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

        process.Start();

        await process.WaitForExitAsync(cancellationToken);

        if (!File.Exists(tempFile))
            throw new Exception("Snapshot failed.");

        var bytes = await File.ReadAllBytesAsync(
            tempFile,
            cancellationToken);

        File.Delete(tempFile);

        return new MemoryStream(bytes);
    }
}


