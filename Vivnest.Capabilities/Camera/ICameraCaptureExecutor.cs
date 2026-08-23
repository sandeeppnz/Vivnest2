using Vivnest.Runtime.State;
using Vivnest.Core.Options;

namespace Vivnest.Capabilities.Camera;

public interface ICameraCaptureExecutor
{
    Task CaptureAsync(
        DeviceOptions cameraOptions,
        DeviceRuntimeState runtime,
        CancellationToken cancellationToken,
        string? triggerReason = null);
}
