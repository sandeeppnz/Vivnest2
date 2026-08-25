namespace Vivnest.Cloud.Interfaces;

public interface IBlobStorageService
{
    Task<byte[]> DownloadAsync(
        string container,
        string blobName,
        CancellationToken cancellationToken = default);

    Uri GenerateReadSasUri(
        string containerName,
        string blobName,
        TimeSpan validFor,
        string? cacheControl = null);

    // Used by IDeviceCapabilitiesQueryService's reverse trigger lookup
    // (decision-log.md ADR-040) - listing device-config to find which
    // other devices' Trigger.DeviceIds include a given deviceId.
    Task<IReadOnlyList<string>> ListBlobNamesAsync(
        string containerName,
        CancellationToken cancellationToken = default);
}
