using Vivnest.Core.Models.Camera;

public interface ICapturePublisher
{
    Task PublishAsync(
        CaptureResult capture,
        CancellationToken cancellationToken = default);
}