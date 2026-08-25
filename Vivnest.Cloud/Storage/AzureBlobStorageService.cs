using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.Azure;

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

    public Uri GenerateReadSasUri(
        string containerName,
        string blobName,
        TimeSpan validFor,
        string? cacheControl = null)
    {
        return _client.GenerateReadSasUri(containerName, blobName, validFor, cacheControl);
    }

    public Task<IReadOnlyList<string>> ListBlobNamesAsync(
        string containerName,
        CancellationToken cancellationToken = default)
    {
        return _client.ListBlobNamesAsync(containerName, cancellationToken);
    }
}