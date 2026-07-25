using Vivnest.Core.Models.Camera;

namespace Vivnest.Agent.Services;

public interface ICaptureService
{
    Task<CaptureResult> CaptureAsync(
        CancellationToken cancellationToken);
}
