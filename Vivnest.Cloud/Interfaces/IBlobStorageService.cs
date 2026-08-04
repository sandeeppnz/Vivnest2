namespace Vivnest.Cloud.Interfaces;

public interface IBlobStorageService
{
    Task<byte[]> DownloadAsync(
        string container,
        string blobName,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(
       string containerName,
       string blobName,
       CancellationToken cancellationToken = default);

    Uri GenerateReadSasUri(
        string containerName,
        string blobName,
        TimeSpan validFor,
        string? cacheControl = null);
}
