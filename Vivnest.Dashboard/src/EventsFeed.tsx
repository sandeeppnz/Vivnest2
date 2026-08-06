import { useEffect, useMemo, useState } from "react";
import { ApiError, getDevices, getEvents, type DeviceEvent, type DeviceSummary } from "./api";
import { describeEvent, isDuplicatedElsewhere } from "./eventDescriptions";
import { formatDateTime, formatDateTimeExact } from "./format";
import { DeviceIcon } from "./icons";

interface EventsFeedProps {
  apiKey: string;
  onSelectDevice: (deviceId: string) => void;
  onAuthError: () => void;
}

// EventSeverity (Vivnest.Core.Enums) has three values - map onto the same
// status-tinted badge vocabulary the rest of the dashboard already uses,
// rather than introduce a fourth color scheme just for this list.
function badgeClassForSeverity(severity: string): string {
  if (severity === "Critical") return "icon-badge-error";
  if (severity === "Warning") return "icon-badge-warning";
  return "icon-badge-unknown";
}

export function EventsFeed({ apiKey, onSelectDevice, onAuthError }: EventsFeedProps) {
  const [events, setEvents] = useState<DeviceEvent[] | null>(null);
  const [devices, setDevices] = useState<DeviceSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    function handleError(err: unknown) {
      if (cancelled) return;

      if (err instanceof ApiError && err.status === 401) {
        onAuthError();
        return;
      }

      setError(err instanceof Error ? err.message : "Failed to load events.");
    }

    getEvents(apiKey)
      .then((result) => !cancelled && setEvents(result.filter((e) => !isDuplicatedElsewhere(e))))
      .catch(handleError);

    getDevices(apiKey)
      .then((result) => !cancelled && setDevices(result))
      .catch(handleError);

    return () => {
      cancelled = true;
    };
  }, [apiKey, onAuthError]);

  const devicesById = useMemo(() => {
    const map = new Map<string, DeviceSummary>();
    for (const device of devices ?? []) {
      map.set(device.deviceId, device);
    }
    return map;
  }, [devices]);

  if (error) return <p className="error">{error}</p>;
  if (!events) return <p>Loading events...</p>;
  if (events.length === 0) return <p>No events yet.</p>;

  return (
    <div className="entity-list">
      {events.map((event, index) => {
        const device = devicesById.get(event.deviceId);

        return (
          <button
            type="button"
            key={index}
            className="entity-row"
            onClick={() => onSelectDevice(event.deviceId)}
          >
            <div className="entity-row-main">
              {event.imageUrl ? (
                <img src={event.imageUrl} alt="" className="row-thumbnail" />
              ) : (
                <span className={`icon-badge ${badgeClassForSeverity(event.severity)}`}>
                  <DeviceIcon deviceType={event.deviceType} className="device-icon" />
                </span>
              )}
              <div>
                <div className="entity-row-title">{describeEvent(event)}</div>
                <div className="entity-row-subtitle">
                  <span>{device?.name || event.deviceId}</span>
                  <span
                    className="entity-row-agent"
                    title={formatDateTimeExact(event.occurredAtUtc)}
                  >
                    {" · "}
                    {formatDateTime(event.occurredAtUtc)}
                  </span>
                </div>
              </div>
            </div>
          </button>
        );
      })}
    </div>
  );
}
