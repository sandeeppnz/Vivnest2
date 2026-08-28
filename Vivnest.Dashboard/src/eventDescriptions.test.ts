import { describe, expect, it } from "vitest";
import type { DeviceEvent } from "./api";
import { describeEvent, isDuplicatedElsewhere } from "./eventDescriptions";

// The payload keys here are PascalCase on purpose - the backend
// serializes .NET property names straight through with no camelCase
// conversion (see CaptureGallery's isTriggeredCapture comment). A test
// written with camelCase keys would pass against nothing real.

function event(eventType: string, data: unknown): DeviceEvent {
  return {
    deviceId: "device-1",
    deviceType: "MotionSensor",
    eventType,
    severity: "Information",
    occurredAtUtc: "2026-08-29T00:00:00Z",
    data,
    imageUrl: null,
    sinkCleanlinessResult: null,
    detectedObjects: null,
  };
}

describe("describeEvent", () => {
  it("reads the motion direction out of Data.Detected", () => {
    expect(describeEvent(event("MotionDetected", { Detected: true }))).toBe("Motion detected");
    expect(describeEvent(event("MotionDetected", { Detected: false }))).toBe("Motion cleared");
    // No direction in the payload: fall back to the raw type rather
    // than guessing a direction.
    expect(describeEvent(event("MotionDetected", {}))).toBe("MotionDetected");
  });

  it("only reads a sink transition as an action when Changed is true", () => {
    expect(describeEvent(event("SinkCleanliness", { Clean: true, Changed: true }))).toBe("Sink cleaned");
    expect(describeEvent(event("SinkCleanliness", { Clean: false, Changed: true }))).toBe("Sink needs cleaning");
    expect(describeEvent(event("SinkCleanliness", { Clean: true }))).toBe("Sink clean");
    expect(describeEvent(event("SinkCleanliness", { Clean: false }))).toBe("Sink still dirty");
  });

  it("ranks object detections: unusual, then person, then the list", () => {
    expect(
      describeEvent(event("ObjectsDetected", {
        Objects: [{ ClassName: "bear", Unusual: true }, { ClassName: "person", Unusual: false }],
        PersonPresent: true,
      })),
    ).toBe("Unusual object: bear");

    expect(
      describeEvent(event("ObjectsDetected", {
        Objects: [{ ClassName: "person", Unusual: false }],
        PersonPresent: true,
      })),
    ).toBe("Person detected");

    expect(
      describeEvent(event("ObjectsDetected", {
        Objects: [{ ClassName: "cat", Unusual: false }, { ClassName: "chair", Unusual: false }],
      })),
    ).toBe("Objects: cat, chair");

    expect(describeEvent(event("ObjectsDetected", { Objects: [] }))).toBe("No objects detected");
  });

  it("falls back to the raw event type for anything unrecognized", () => {
    expect(describeEvent(event("SomethingNew", { Whatever: 1 }))).toBe("SomethingNew");
  });
});

describe("isDuplicatedElsewhere", () => {
  it("hides only BatteryStatus, which has its own section", () => {
    expect(isDuplicatedElsewhere(event("BatteryStatus", {}))).toBe(true);
    expect(isDuplicatedElsewhere(event("MotionDetected", {}))).toBe(false);
  });
});
