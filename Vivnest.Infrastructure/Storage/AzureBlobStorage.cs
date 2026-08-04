using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using Vivnest.Core.Options;
using Vivnest.Core.PhotoStores;
using Vivnest.Core.Storage;

namespace Vivnest.Infrastructure.Storage;

public class AzureBlobStorage : IPhotoStorage
{
    // Capture photos are immutable once written - each gets a unique,
    // timestamp-based blob name (IBlobNameGenerator), never overwritten
    // with different content - so caching forever is safe. Unlike the
    // config/log blobs (which do get overwritten repeatedly), this is the
    // one blob type in this codebase where "immutable" is actually true,
    // not just assumed.
    private static readonly BlobHttpHeaders CaptureHeaders = new()
    {
        ContentType = "image/jpeg",
        CacheControl = "public, max-age=31536000, immutable",
    };

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
            CaptureHeaders,
            cancellationToken);
    }
}
