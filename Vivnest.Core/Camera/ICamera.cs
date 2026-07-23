namespace Vivnest.Core.Camera;

public interface ICamera
{
    Task<Stream> CaptureAsync(
        CancellationToken cancellationToken = default);
}