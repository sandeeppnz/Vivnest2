using Microsoft.Extensions.Options;
using Vivnest.Core.Interfaces;
using Vivnest.Core.Options;

namespace Vivnest.Infrastructure.Camera;

public class TapoC120Camera : ICamera
{
    private readonly RtspCamera _rtspCamera;

    public TapoC120Camera(RtspCamera rtspCamera)
    {
        _rtspCamera = rtspCamera;
    }

    public Task<Stream> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        return _rtspCamera.CaptureAsync(cancellationToken);
    }
}