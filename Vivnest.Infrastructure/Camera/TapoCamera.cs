using Vivnest.Core.Camera;

namespace Vivnest.Infrastructure.Camera;

public class TapoCamera : ICamera
{
    public Task<Stream> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}