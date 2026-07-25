using Vivnest.Core.Models.Camera;
using Vivnest.Core.Options;

namespace Vivnest.Agent.Services;

public interface ICaptureService
{
    Task<CaptureResult> CaptureAsync(
        DeviceOptions cameraOptions,
        CancellationToken cancellationToken);
}
