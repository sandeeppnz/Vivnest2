using Vivnest.Core.MotionSensor.Models;

namespace Vivnest.Capabilities.MotionSensor;

public sealed record MotionSensorBatteryReportedEvent(
    MotionSensorReadingResult Result);
