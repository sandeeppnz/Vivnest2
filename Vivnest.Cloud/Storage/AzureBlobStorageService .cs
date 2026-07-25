using Azure.Storage.Blobs;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Vivnest.Cloud.Interfaces;

namespace Vivnest.Cloud.Storage;

public sealed class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _blobServiceClient;

    public AzureBlobStorageService(
        BlobServiceClient blobServiceClient)
    {
        _blobServiceClient = blobServiceClient;
    }

    public async Task<byte[]> DownloadAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        var container = _blobServiceClient.GetBlobContainerClient(containerName);

        var blob = container.GetBlobClient(blobName);

        var response = await blob.DownloadContentAsync(cancellationToken);

        return response.Value.Content.ToArray();
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
    public async Task<Stream> OpenReadAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        var container = _blobServiceClient.GetBlobContainerClient(containerName);

        var blob = container.GetBlobClient(blobName);

        var response = await blob.DownloadStreamingAsync(
            cancellationToken: cancellationToken);

        return response.Value.Content;
    }
}