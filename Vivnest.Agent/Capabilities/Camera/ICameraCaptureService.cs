using Vivnest.Core.Camera.Models;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Capabilities.Camera;

public interface ICameraCaptureService
{
    Task<CameraCaptureResult> CaptureAsync(
        DeviceOptions cameraOptions,
        CancellationToken cancellationToken);

    Task<bool> CheckReachabilityAsync(
        DeviceOptions cameraOptions,
        CancellationToken cancellationToken);
}
