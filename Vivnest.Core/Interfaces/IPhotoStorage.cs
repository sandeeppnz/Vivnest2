namespace Vivnest.Core.Interfaces;

public interface IPhotoStorage
{
    Task UploadAsync(
        Stream image,
        string blobName,
        CancellationToken cancellationToken = default);
}
