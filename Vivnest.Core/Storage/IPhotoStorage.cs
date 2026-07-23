namespace Vivnest.Core.Storage;

public interface IPhotoStorage
{
    Task UploadAsync(
        Stream image,
        DateTime capturedAt,
        CancellationToken cancellationToken = default);
}