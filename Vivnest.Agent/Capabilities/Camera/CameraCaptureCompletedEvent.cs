using Vivnest.Core.Camera.Models;

namespace Vivnest.Agent.Capabilities.Camera;

public sealed record CameraCaptureCompletedEvent(
  CameraCaptureResult Result,
  string? TriggerReason = null);
