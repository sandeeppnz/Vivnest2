using Vivnest.Core.Models;

namespace Vivnest.Agent.Services;

public interface ICaptureService
{
    Task<CaptureResult> CaptureAsync(
        CancellationToken cancellationToken);
}
