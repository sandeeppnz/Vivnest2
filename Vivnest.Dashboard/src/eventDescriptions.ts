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

  // SinkCleanlinessWorker persists one of these on every classification,
  // not just transitions (ADR-034's follow-up) - Changed distinguishes a
  // real clean<->dirty flip from a repeat "still clean"/"still dirty"
  // reading, since only the former should read as an action ("cleaned").
  if (event.eventType === "SinkCleanliness") {
    const data = event.data;
    const clean =
      typeof data === "object" && data !== null
        ? (data as { Clean?: unknown }).Clean
        : undefined;
    const changed =
      typeof data === "object" && data !== null
        ? (data as { Changed?: unknown }).Changed
        : undefined;

    if (typeof clean === "boolean") {
      if (changed === true) {
        return clean ? "Sink cleaned" : "Sink needs cleaning";
      }

      return clean ? "Sink clean" : "Sink still dirty";
    }
  }

  // SinkCleanlinessWorker.FlagUnusualObjectsAsync - Data.Objects is a list
  // of { ClassName, Confidence }, not a single value like the two cases
  // above, so this lists every class name found rather than branching on
  // one boolean.
  if (event.eventType === "UnusualObjectDetected") {
    const data = event.data;
    const objects =
      typeof data === "object" && data !== null
        ? (data as { Objects?: unknown }).Objects
        : undefined;

    if (Array.isArray(objects) && objects.length > 0) {
      const names = objects
        .map((o) => (typeof o === "object" && o !== null ? (o as { ClassName?: unknown }).ClassName : undefined))
        .filter((name): name is string => typeof name === "string");

      if (names.length > 0) {
        return `Unusual object: ${names.join(", ")}`;
      }
    }
  }

  return event.eventType;
}
