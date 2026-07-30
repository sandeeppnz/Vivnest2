using Vivnest.Core.Camera.Models;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Interfaces;

public interface ICameraCaptureService
{
    Task<CameraCaptureResult> CaptureAsync(
        DeviceOptions cameraOptions,
        CancellationToken cancellationToken);
}
