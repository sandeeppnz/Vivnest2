using Vivnest.Abstractions.Models.Camera;

namespace Vivnest.Capabilities.Camera.Events;

public sealed record CameraCaptureFailedEvent(
    CameraCaptureFailureData Failure);


