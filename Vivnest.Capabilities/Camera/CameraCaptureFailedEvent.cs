using Vivnest.Core.Camera.Models;

namespace Vivnest.Capabilities.Camera;

public sealed record CameraCaptureFailedEvent(
    CameraCaptureFailureData Failure);


