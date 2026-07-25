using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Options;

namespace Vivnest.Infrastructure.Storage;

public class AzureBlobStorage : IPhotoStorage
{
    private readonly BlobContainerClient _container;

    public AzureBlobStorage(IOptions<StorageOptions> options)
    {
        var client = new BlobServiceClient(options.Value.ConnectionString);

        _container = client.GetBlobContainerClient(options.Value.ContainerName);

        _container.CreateIfNotExists(PublicAccessType.None);
    }

    public async Task UploadAsync(
        Stream image,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        var blob = _container.GetBlobClient(blobName);

        await blob.UploadAsync(
            image,
            overwrite: true,
            cancellationToken);
    }
}