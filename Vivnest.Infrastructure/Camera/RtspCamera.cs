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
            $"rtsp://{_options.Settings.RtspUsername}:{_options.Settings.RtspPassword}" +
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

        process.StartInfo.Arguments =
            $"-y -rtsp_transport tcp -i \"{rtspUrl}\" " +
            "-frames:v 1 " +
            $"\"{tempFile}\"";


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


