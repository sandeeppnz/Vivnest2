using Microsoft.Extensions.Hosting;
using Vivnest.Core.Camera;
using Vivnest.Core.Storage;

namespace Vivnest.Agent.Services;

public class CaptureService : IHostedService
{
    private readonly IPhotoStorage _storage;
    private readonly ICamera _camera;

    public CaptureService(ICamera camera,
        IPhotoStorage storage)
    {
        _storage = storage;
        _camera = camera;
    }

    public async Task StartAsync(
        CancellationToken cancellationToken)
    {
        Console.WriteLine("Uploading test image...");

        //using var stream =
        //    File.OpenRead("sample.jpg");

        //await _storage.UploadAsync(
        //    stream,
        //    DateTime.Now,
        //    cancellationToken);

        using var stream = await _camera.CaptureAsync(cancellationToken);

        await _storage.UploadAsync(
            stream,
            DateTime.UtcNow,
            cancellationToken);

        Console.WriteLine("Finished upload.");
    }

    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}