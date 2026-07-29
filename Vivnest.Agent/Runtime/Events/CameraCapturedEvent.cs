using System;
using System.Collections.Generic;
using System.Text;
using Vivnest.Core.Models.Camera;

namespace Vivnest.Agent.Runtime.Events;

public sealed record CameraCapturedEvent(
  CaptureResult Result);


