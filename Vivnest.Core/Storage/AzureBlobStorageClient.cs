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
        BlobHttpHeaders? httpHeaders = null,
        CancellationToken cancellationToken = default)
    {
        var container = _blobServiceClient.GetBlobContainerClient(containerName);

        await container.CreateIfNotExistsAsync(
            PublicAccessType.None,
            cancellationToken: cancellationToken);

        var blob = container.GetBlobClient(blobName);

        // BlobUploadOptions with no Conditions set is the unconditional-overwrite
        // behavior the old UploadAsync(content, overwrite: true, ...) convenience
        // overload gave - kept identical, just routed through the options object
        // so callers can also set HttpHeaders (Cache-Control, Content-Type).
        await blob.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = httpHeaders },
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

    // Lists blob names in a container - used at Capture-agent startup
    // (decision-log.md ADR-036) to discover device-config blobs, since
    // there's no way to know which device files exist ahead of time.
    // Callers filter the results themselves (e.g. by an owning-agent field
    // inside each blob); no server-side filtering happens here.
    public async Task<IReadOnlyList<string>> ListBlobNamesAsync(
        string containerName,
        CancellationToken cancellationToken = default)
    {
        var container = _blobServiceClient.GetBlobContainerClient(containerName);
        var names = new List<string>();

        await foreach (var blobItem in container.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            names.Add(blobItem.Name);
        }

        return names;
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
        TimeSpan validFor,
        string? cacheControl = null)
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

        // Response-header override (the `rscc` SAS query param), not a
        // change to the blob's own stored headers - lets already-uploaded
        // blobs (with no Cache-Control of their own) still get served
        // cacheable through this URL. Left null for callers whose content
        // changes over time (e.g. the log blob, ADR-027) - only capture
        // images, immutable once written, pass this.
        if (cacheControl is not null)
            sasBuilder.CacheControl = cacheControl;

        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        return blob.GenerateSasUri(sasBuilder);
    }
}
