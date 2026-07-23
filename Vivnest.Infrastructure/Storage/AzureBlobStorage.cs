using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;

namespace Vivnest.Infrastructure.Storage;

public class AzureBlobStorage : IPhotoStorage
{
    private readonly BlobContainerClient _container;

    public AzureBlobStorage(IOptions<StorageOptions> options)
    {
        var client = new BlobServiceClient(options.Value.ConnectionString);

        _container = client.GetBlobContainerClient(options.Value.Container);

        _container.CreateIfNotExists(PublicAccessType.None);
    }

    public async Task UploadAsync(
        Stream image,
        DateTime capturedAt,
        CancellationToken cancellationToken = default)
    {
        var blobName =
            $"LivingRoom/{capturedAt:yyyy/MM/dd/HH-mm-ss}.jpg";

        var blob = _container.GetBlobClient(blobName);

        await blob.UploadAsync(
            image,
            overwrite: true,
            cancellationToken);
    }
}