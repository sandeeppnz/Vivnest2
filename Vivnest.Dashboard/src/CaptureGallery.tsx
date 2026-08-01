import { useEffect, useState } from "react";
import { ApiError, getDeviceCapturesTimeline, type DeviceEvent } from "./api";

const TIMELINE_DAYS = 30;

interface CaptureGalleryProps {
  apiKey: string;
  deviceId: string;
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

export function CaptureGallery({ apiKey, deviceId, onAuthError }: CaptureGalleryProps) {
  const [captures, setCaptures] = useState<DeviceEvent[] | null>(null);
  const [selectedCapture, setSelectedCapture] = useState<DeviceEvent | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    getDeviceCapturesTimeline(apiKey, deviceId, TIMELINE_DAYS)
      .then((result) => {
        if (cancelled) return;
        setCaptures(result);
        setSelectedCapture(result[0] ?? null);
      })
      .catch((err) => {
        if (cancelled) return;

        if (err instanceof ApiError && err.status === 401) {
          onAuthError();
          return;
        }

        setError(err instanceof Error ? err.message : "Failed to load captures.");
      });

    return () => {
      cancelled = true;
    };
  }, [apiKey, deviceId, onAuthError]);

  if (error) return <p className="error">{error}</p>;
  if (!captures) return <p>Loading captures...</p>;
  if (captures.length === 0) return <p>No captures in the last {TIMELINE_DAYS} days.</p>;

  const groups = groupByDate(captures);

  return (
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
  );
}
