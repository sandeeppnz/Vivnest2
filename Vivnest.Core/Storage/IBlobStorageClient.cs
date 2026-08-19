using Azure.Storage.Blobs.Models;

namespace Vivnest.Core.Storage;

// Extracted from AzureBlobStorageClient so the code that writes runtime
// configuration can be tested at all.
//
// The two configuration publishers hold the most intricate logic in this
// codebase - a bounded retry loop, immutable version blobs guarded by
// failIfExists, an ETag-guarded metadata row, a content-hash no-op check,
// and rollback-as-a-new-version - and every one of those behaviours was
// unreachable from a test, because the dependency was a concrete class
// wrapping BlobServiceClient. Nothing was wrong with the class; it just
// had no seam.
//
// Deliberately mirrors the concrete client's existing surface exactly
// rather than trimming it to the publishers' needs, so every other
// consumer can adopt it without a second interface appearing later. This
// is *not* a duplicate of Vivnest.Cloud's IBlobStorageService: that one is
// read-only by design (no Upload at all) and serves the query/handler
// layer. This is the full client contract, and it lives in Core because
// both Agent and Cloud already depend on the concrete type it describes.
public interface IBlobStorageClient
{
    // httpHeaders carries Cache-Control/Content-Type for capture blobs
    // (AzureBlobStorage.CaptureHeaders); the configuration publishers pass
    // null. It is an Azure SDK type, which is fine here - Core already
    // references Azure.Storage.Blobs, and mirroring the concrete signature
    // exactly is what lets the existing class implement this with no
    // changes to it at all.
    Task UploadAsync(
        string containerName,
        string blobName,
        Stream content,
        BlobHttpHeaders? httpHeaders = null,
        bool failIfExists = false,
        CancellationToken cancellationToken = default);

    Task<byte[]> DownloadAsync(
        string containerName,
        string blobName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListBlobNamesAsync(
        string containerName,
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
