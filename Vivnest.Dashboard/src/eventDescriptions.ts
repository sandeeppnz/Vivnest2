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

  // SinkCleanlinessWorker.PersistObjectDetectionEventAsync fires on every
  // capture ObjectDetection runs, not just ones with something unusual
  // (same "every classification" shape as SinkCleanliness above) - Data.Objects
  // is a list of { ClassName, Confidence, X1..Y2, Unusual }, so this
  // branches on whether any entry is Unusual rather than a single boolean.
  if (event.eventType === "ObjectsDetected") {
    const data = event.data;
    const objects =
      typeof data === "object" && data !== null
        ? (data as { Objects?: unknown }).Objects
        : undefined;
    const personPresent =
      typeof data === "object" && data !== null
        ? (data as { PersonPresent?: unknown }).PersonPresent
        : undefined;

    if (Array.isArray(objects)) {
      type Entry = { className?: unknown; unusual?: unknown };
      const entries = objects
        .filter((o): o is Entry => typeof o === "object" && o !== null)
        .map((o) => ({
          className: (o as { ClassName?: unknown }).ClassName,
          unusual: (o as { Unusual?: unknown }).Unusual,
        }));

      const unusualNames = entries
        .filter((e) => e.unusual === true && typeof e.className === "string")
        .map((e) => e.className as string);

      if (unusualNames.length > 0) {
        return `Unusual object: ${unusualNames.join(", ")}`;
      }

      if (personPresent === true) {
        return "Person detected";
      }

      const usualNames = entries
        .filter((e) => typeof e.className === "string")
        .map((e) => e.className as string);

      return usualNames.length > 0 ? `Objects: ${usualNames.join(", ")}` : "No objects detected";
    }
  }

  return event.eventType;
}
