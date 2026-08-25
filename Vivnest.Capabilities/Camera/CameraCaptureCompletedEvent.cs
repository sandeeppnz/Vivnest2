using Vivnest.Core.Devices.Camera.Models;

namespace Vivnest.Capabilities.Camera;

public sealed record CameraCaptureCompletedEvent(
  CameraCaptureResult Result,
  string? TriggerReason = null);
