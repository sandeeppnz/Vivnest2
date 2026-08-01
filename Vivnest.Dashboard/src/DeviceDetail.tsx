import { useEffect, useState } from "react";
import {
  ApiError,
  getDevice,
  getDeviceCaptures,
  getDeviceEvents,
  type DeviceEvent,
  type DeviceSummary,
} from "./api";

interface DeviceDetailProps {
  apiKey: string;
  deviceId: string;
  onBack: () => void;
  onAuthError: () => void;
}

export function DeviceDetail({ apiKey, deviceId, onBack, onAuthError }: DeviceDetailProps) {
  const [device, setDevice] = useState<DeviceSummary | null>(null);
  const [events, setEvents] = useState<DeviceEvent[] | null>(null);
  const [latestCapture, setLatestCapture] = useState<DeviceEvent | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    function handleError(err: unknown) {
      if (cancelled) return;

      if (err instanceof ApiError && err.status === 401) {
        onAuthError();
        return;
      }

      setError(err instanceof Error ? err.message : "Failed to load device.");
    }

    getDevice(apiKey, deviceId)
      .then((result) => !cancelled && setDevice(result))
      .catch(handleError);

    getDeviceEvents(apiKey, deviceId)
      .then((result) => !cancelled && setEvents(result))
      .catch(handleError);

    getDeviceCaptures(apiKey, deviceId, 1)
      .then((result) => !cancelled && setLatestCapture(result[0] ?? null))
      .catch(handleError);

    return () => {
      cancelled = true;
    };
  }, [apiKey, deviceId, onAuthError]);

  return (
    <div className="device-detail">
      <button onClick={onBack}>&larr; Back to devices</button>
      <h2>{deviceId}</h2>

      {error && <p className="error">{error}</p>}

      {device && (
        <dl className="device-summary">
          <dt>Status</dt>
          <dd>
            <span className={`status status-${device.status.toLowerCase()}`}>
              {device.status}
            </span>
          </dd>
          <dt>Type</dt>
          <dd>{device.deviceType}</dd>
          <dt>Last heartbeat</dt>
          <dd>{new Date(device.lastHeartbeatUtc).toLocaleString()}</dd>
          <dt>Last activity</dt>
          <dd>{device.lastActivityUtc ? new Date(device.lastActivityUtc).toLocaleString() : "—"}</dd>
          {device.error && (
            <>
              <dt>Error</dt>
              <dd className="error">{device.error}</dd>
            </>
          )}
        </dl>
      )}

      <h3>Latest image</h3>
      {latestCapture?.imageUrl ? (
        <img className="latest-image" src={latestCapture.imageUrl} alt={`Latest capture for ${deviceId}`} />
      ) : (
        <p>No captures yet.</p>
      )}

      <h3>Recent events</h3>
      {!events ? (
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
    </div>
  );
}
