using Vivnest.Core.Camera.Models;

namespace Vivnest.Agent.Capabilities.Camera;

public sealed record CameraCaptureFailedEvent(
    CameraCaptureFailureData Failure);


