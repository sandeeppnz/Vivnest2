import { useEffect, useState } from "react";
import {
  ApiError,
  getDevice,
  getDeviceCapturesTimeline,
  getDeviceEvents,
  type DeviceEvent,
  type DeviceSummary,
} from "./api";

const TIMELINE_DAYS = 30;

interface DeviceDetailProps {
  apiKey: string;
  deviceId: string;
  onBack: () => void;
  onAuthError: () => void;
}

interface CaptureGroup {
  dateKey: string;
  heading: string;
  captures: DeviceEvent[];
}

function dateHeading(date: Date): string {
  const today = new Date();
  const yesterday = new Date();
  yesterday.setDate(today.getDate() - 1);

  if (date.toDateString() === today.toDateString()) return "Today";
  if (date.toDateString() === yesterday.toDateString()) return "Yesterday";

  return date.toLocaleDateString(undefined, {
    weekday: "long",
    month: "long",
    day: "numeric",
    year: date.getFullYear() === today.getFullYear() ? undefined : "numeric",
  });
}

function groupByDate(captures: DeviceEvent[]): CaptureGroup[] {
  const groups: CaptureGroup[] = [];
  const groupsByKey = new Map<string, CaptureGroup>();

  for (const capture of captures) {
    const date = new Date(capture.occurredAtUtc);
    const dateKey = date.toDateString();

    let group = groupsByKey.get(dateKey);

    if (!group) {
      group = { dateKey, heading: dateHeading(date), captures: [] };
      groupsByKey.set(dateKey, group);
      groups.push(group);
    }

    group.captures.push(capture);
  }

  return groups;
}

export function DeviceDetail({ apiKey, deviceId, onBack, onAuthError }: DeviceDetailProps) {
  const [device, setDevice] = useState<DeviceSummary | null>(null);
  const [events, setEvents] = useState<DeviceEvent[] | null>(null);
  const [captures, setCaptures] = useState<DeviceEvent[] | null>(null);
  const [selectedCapture, setSelectedCapture] = useState<DeviceEvent | null>(null);
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

    getDeviceCapturesTimeline(apiKey, deviceId, TIMELINE_DAYS)
      .then((result) => {
        if (cancelled) return;
        setCaptures(result);
        setSelectedCapture(result[0] ?? null);
      })
      .catch(handleError);

    return () => {
      cancelled = true;
    };
  }, [apiKey, deviceId, onAuthError]);

  const groups = captures ? groupByDate(captures) : [];

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

      <h3>Captures</h3>
      {!captures ? (
        <p>Loading captures...</p>
      ) : captures.length === 0 ? (
        <p>No captures in the last {TIMELINE_DAYS} days.</p>
      ) : (
        <>
          {selectedCapture?.imageUrl && (
            <>
              <img
                className="latest-image"
                src={selectedCapture.imageUrl}
                alt={`Capture from ${deviceId} at ${selectedCapture.occurredAtUtc}`}
              />
              <p className="capture-caption">
                {new Date(selectedCapture.occurredAtUtc).toLocaleString()}
              </p>
            </>
          )}

          <div className="captures-timeline">
            {groups.map((group) => (
              <div className="timeline-date-section" key={group.dateKey}>
                <h4 className="timeline-date-heading">
                  {group.heading}
                  <span className="timeline-date-count"> ({group.captures.length})</span>
                </h4>
                <div className="capture-gallery">
                  {group.captures.map((capture) => (
                    <button
                      key={capture.occurredAtUtc}
                      type="button"
                      className={`capture-thumb${capture === selectedCapture ? " selected" : ""}`}
                      onClick={() => setSelectedCapture(capture)}
                      title={new Date(capture.occurredAtUtc).toLocaleString()}
                    >
                      {capture.imageUrl && <img src={capture.imageUrl} alt="" />}
                    </button>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </>
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
