using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Storage;

namespace Vivnest.Cloud.Storage;

public sealed class AzureBlobStorageService : IBlobStorageService
{
    private readonly AzureBlobStorageClient _client;

    public AzureBlobStorageService(
        AzureBlobStorageClient client)
    {
        _client = client;
    }

    public Task<byte[]> DownloadAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        return _client.DownloadAsync(
            containerName,
            blobName,
            cancellationToken);
    }

    /// <summary>
    /// Returning a byte[] is fine for Telegram because the images are relatively small. However, if you later start storing:
    //4K images
    //50 MB videos
    //AI processing batches
    //then loading everything into memory isn't ideal.
    /// </summary>
    /// <param name="containerName"></param>
    /// <param name="blobName"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<Stream> OpenReadAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        return _client.OpenReadAsync(
            containerName,
            blobName,
            cancellationToken);
    }

    public Uri GenerateReadSasUri(
        string containerName,
        string blobName,
        TimeSpan validFor,
        string? cacheControl = null)
    {
        return _client.GenerateReadSasUri(containerName, blobName, validFor, cacheControl);
    }
}