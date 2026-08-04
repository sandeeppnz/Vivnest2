using Vivnest.Core.MotionSensor.Models;

namespace Vivnest.Agent.Capabilities.MotionSensor;

public sealed record MotionSensorReadingFailedEvent(
    MotionSensorReadingFailureData Failure);
