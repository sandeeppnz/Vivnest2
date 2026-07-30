using Microsoft.Extensions.Options;
using Vivnest.Core.Options;
using Vivnest.Core.PhotoStores;
using Vivnest.Core.Storage;

namespace Vivnest.Infrastructure.Storage;

public class AzureBlobStorage : IPhotoStorage
{
    private readonly AzureBlobStorageClient _client;
    private readonly string _containerName;

    public AzureBlobStorage(
        AzureBlobStorageClient client,
        IOptions<StorageOptions> options)
    {
        _client = client;
        _containerName = options.Value.BlobContainer;
    }

    public Task UploadAsync(
        Stream image,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        return _client.UploadAsync(
            _containerName,
            blobName,
            image,
            cancellationToken);
    }
}
