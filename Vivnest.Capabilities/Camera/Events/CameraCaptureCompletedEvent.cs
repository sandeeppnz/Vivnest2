using Vivnest.Abstractions.Models.Camera;

namespace Vivnest.Capabilities.Camera.Events;

public sealed record CameraCaptureCompletedEvent(
  CameraCaptureResult Result,
  string? TriggerReason = null);


