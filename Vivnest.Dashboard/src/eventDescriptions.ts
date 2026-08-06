import type { DeviceEvent } from "./api";

// BatteryStatus already has its own dedicated section (BatteryStatus.tsx)
// on the motion sensor's device detail page - showing it again in an event
// list would just duplicate it.
export function isDuplicatedElsewhere(event: DeviceEvent): boolean {
  return event.eventType === "BatteryStatus";
}

// MotionSensorStateChangedHandler persists the same EventType
// ("MotionDetected") for both the detected and cleared transitions - the
// direction only lives in Data.Detected, so it has to be read out here to
// label the row correctly instead of showing "MotionDetected" for both.
export function describeEvent(event: DeviceEvent): string {
  if (event.eventType === "MotionDetected") {
    const data = event.data;
    const detected =
      typeof data === "object" && data !== null
        ? (data as { Detected?: unknown }).Detected
        : undefined;

    if (typeof detected === "boolean") {
      return detected ? "Motion detected" : "Motion cleared";
    }
  }

  return event.eventType;
}
