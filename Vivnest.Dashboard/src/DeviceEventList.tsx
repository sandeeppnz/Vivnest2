import { useEffect, useState } from "react";
import { ApiError, getDeviceEvents, type DeviceEvent } from "./api";

interface DeviceEventListProps {
  apiKey: string;
  deviceId: string;
  onAuthError: () => void;
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
        if (!cancelled) setEvents(result);
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
      <h3>Recent events</h3>
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
              <span className="event-type">{event.eventType}</span>
              <span className="event-time">{new Date(event.occurredAtUtc).toLocaleString()}</span>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
