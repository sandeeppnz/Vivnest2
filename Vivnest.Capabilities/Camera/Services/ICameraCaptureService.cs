using Vivnest.Core.Options;
using Vivnest.Abstractions.Models.Camera;

namespace Vivnest.Capabilities.Camera.Services;

public interface ICameraCaptureService
{
    Task<CameraCaptureResult> CaptureAsync(
        DeviceOptions cameraOptions,
        CancellationToken cancellationToken);

    Task<bool> CheckReachabilityAsync(
        DeviceOptions cameraOptions,
        CancellationToken cancellationToken);
}
