using Microsoft.Extensions.Hosting;
using Vivnest.Core.Storage;

namespace Vivnest.Agent.Services;

public class UploadTestService : IHostedService
{
    private readonly IPhotoStorage _storage;

    public UploadTestService(
        IPhotoStorage storage)
    {
        _storage = storage;
    }

    public async Task StartAsync(
        CancellationToken cancellationToken)
    {
        Console.WriteLine("Uploading test image...");

        using var stream =
            File.OpenRead("sample.jpg");

        await _storage.UploadAsync(
            stream,
            DateTime.Now,
            cancellationToken);

        Console.WriteLine("Finished upload.");
    }

    public Task StopAsync(
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}