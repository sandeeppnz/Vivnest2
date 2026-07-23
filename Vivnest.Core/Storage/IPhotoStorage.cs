namespace Vivnest.Core.Storage;

public interface IPhotoStorage
{
    Task UploadAsync(
        Stream image,
        string blobName,
        CancellationToken cancellationToken = default);
}
