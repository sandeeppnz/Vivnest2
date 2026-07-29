using System;
using System.Collections.Generic;
using System.Text;

namespace Vivnest.Agent.Runtime.Events;

public sealed record CameraCaptureFailedEvent(
    string DeviceId,
    DateTime TimestampUtc,
    string? Error
);
