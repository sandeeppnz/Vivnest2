using Vivnest.Core.Devices.MotionSensor.Models;

namespace Vivnest.Capabilities.MotionSensor;

public sealed record MotionSensorReadingFailedEvent(
    MotionSensorReadingFailureData Failure);
