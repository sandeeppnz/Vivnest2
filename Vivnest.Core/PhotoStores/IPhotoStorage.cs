namespace Vivnest.Core.PhotoStores;

public interface IPhotoStorage
{
    Task UploadAsync(
        Stream image,
        string blobName,
        CancellationToken cancellationToken = default);
}
