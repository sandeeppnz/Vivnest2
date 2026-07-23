using Microsoft.Extensions.Options;
using System.Diagnostics;
using Vivnest.Core.Camera;
using Vivnest.Core.Options;

namespace Vivnest.Infrastructure.Camera;

public class RtspCamera : ICamera
{
    private readonly CameraOptions _options;

    public RtspCamera(IOptions<CameraOptions> options)
    {
        _options = options.Value;
    }

    public async Task<Stream> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var tempFile = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid()}.jpg");

        var rtspUrl =
            $"rtsp://{_options.RtspUsername}:{_options.RtspPassword}" +
            $"@{_options.Host}:554/stream1";

        var ffmpegPath = Path.Combine(
            AppContext.BaseDirectory,
            "Tools",
            "ffmpeg.exe");

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
