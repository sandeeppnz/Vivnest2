using Vivnest.Core.Camera.Stores;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Capabilities.Camera;

public interface ICameraCaptureExecutor
{
    Task CaptureAsync(
        DeviceOptions cameraOptions,
        DeviceRuntimeState runtime,
        CancellationToken cancellationToken,
        string? triggerReason = null);
}
