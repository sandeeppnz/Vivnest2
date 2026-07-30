using Vivnest.Core.Camera.Models;

namespace Vivnest.Agent.Runtime.Events;

public sealed record CameraCaptureCompletedEvent(
  CameraCaptureResult Result);


