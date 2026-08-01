using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

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

    public Uri GenerateReadSasUri(
        string containerName,
        string blobName,
        TimeSpan validFor)
    {
        var blob = _blobServiceClient
            .GetBlobContainerClient(containerName)
            .GetBlobClient(blobName);

        if (!blob.CanGenerateSasUri)
            throw new InvalidOperationException(
                "The current BlobServiceClient cannot generate SAS URIs; it must be constructed from a shared-key connection string.");

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = containerName,
            BlobName = blobName,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.Add(validFor)
        };

        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        return blob.GenerateSasUri(sasBuilder);
    }
}
