namespace Vivnest.Core.Interfaces;

public interface ICamera
{
    Task<Stream> CaptureAsync(
        CancellationToken cancellationToken = default);
}