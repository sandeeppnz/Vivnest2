using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Vivnest.Core.Storage;

public sealed class AzureBlobStorageClient
{
    private readonly BlobServiceClient _blobServiceClient;

    public AzureBlobStorageClient(BlobServiceClient blobServiceClient)
    {
        _blobServiceClient = blobServiceClient;
    }

    public async Task UploadAsync(
        string containerName,
        string blobName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var container = _blobServiceClient.GetBlobContainerClient(containerName);

        await container.CreateIfNotExistsAsync(
            PublicAccessType.None,
            cancellationToken: cancellationToken);

        var blob = container.GetBlobClient(blobName);

        await blob.UploadAsync(
            content,
            overwrite: true,
            cancellationToken);
    }

    public async Task<byte[]> DownloadAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        var blob = _blobServiceClient
            .GetBlobContainerClient(containerName)
            .GetBlobClient(blobName);

        var response = await blob.DownloadContentAsync(cancellationToken);

        return response.Value.Content.ToArray();
    }

    public async Task<Stream> OpenReadAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        var blob = _blobServiceClient
            .GetBlobContainerClient(containerName)
            .GetBlobClient(blobName);

        var response = await blob.DownloadStreamingAsync(
            cancellationToken: cancellationToken);

        return response.Value.Content;
    }
}
