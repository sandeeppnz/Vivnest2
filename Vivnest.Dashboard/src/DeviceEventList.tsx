import { useEffect, useState } from "react";
import { ApiError, getDeviceEvents, type DeviceEvent } from "./api";
import { formatDateTime, formatDateTimeExact } from "./format";

interface DeviceEventListProps {
  apiKey: string;
  deviceId: string;
  onAuthError: () => void;
}

// BatteryStatus already has its own dedicated section (BatteryStatus.tsx)
// on the motion sensor's device detail page - showing it again here would
// just duplicate it.
function isDuplicatedElsewhere(event: DeviceEvent): boolean {
  return event.eventType === "BatteryStatus";
}

// MotionSensorStateChangedHandler persists the same EventType
// ("MotionDetected") for both the detected and cleared transitions - the
// direction only lives in Data.Detected, so it has to be read out here to
// label the row correctly instead of showing "MotionDetected" for both.
function describeEvent(event: DeviceEvent): string {
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

export function DeviceEventList({ apiKey, deviceId, onAuthError }: DeviceEventListProps) {
  const [events, setEvents] = useState<DeviceEvent[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    setEvents(null);
    setError(null);

    getDeviceEvents(apiKey, deviceId)
      .then((result) => {
        if (!cancelled) setEvents(result.filter((event) => !isDuplicatedElsewhere(event)));
      })
      .catch((err) => {
        if (cancelled) return;

        if (err instanceof ApiError && err.status === 401) {
          onAuthError();
          return;
        }

        setError(err instanceof Error ? err.message : "Failed to load events.");
      });

    return () => {
      cancelled = true;
    };
  }, [apiKey, deviceId, onAuthError]);

  return (
    <>
      <h3 className="section-heading">Recent events</h3>
      {error ? (
        <p className="error">{error}</p>
      ) : !events ? (
        <p>Loading events...</p>
      ) : events.length === 0 ? (
        <p>No events yet.</p>
      ) : (
        <ul className="event-list">
          {events.map((event, index) => (
            <li key={index}>
              <span className="event-type">{describeEvent(event)}</span>
              <span className="event-time" title={formatDateTimeExact(event.occurredAtUtc)}>
                {formatDateTime(event.occurredAtUtc)}
              </span>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
