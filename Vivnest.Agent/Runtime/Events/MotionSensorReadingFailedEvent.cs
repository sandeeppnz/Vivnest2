using Vivnest.Core.MotionSensor.Models;

namespace Vivnest.Agent.Runtime.Events;

public sealed record MotionSensorReadingFailedEvent(
    MotionSensorReadingFailureData Failure);
