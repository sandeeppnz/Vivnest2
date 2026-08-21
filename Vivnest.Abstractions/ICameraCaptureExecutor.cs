using Vivnest.Core.Devices.Stores;
using Vivnest.Core.Options;

namespace Vivnest.Abstractions;

public interface ICameraCaptureExecutor
{
    Task CaptureAsync(
        DeviceOptions cameraOptions,
        DeviceRuntimeState runtime,
        CancellationToken cancellationToken,
        string? triggerReason = null);
}
