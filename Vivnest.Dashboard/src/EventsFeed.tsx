import { useMemo, useState } from "react";
import { type DeviceSummary } from "./api";
import { ErrorState } from "./ErrorState";
import { describeEvent, isDuplicatedElsewhere } from "./eventDescriptions";
import { formatDateTime, formatDateTimeExact } from "./format";
import { DeviceIcon } from "./icons";
import { useDevices, useEvents } from "./queries";

interface EventsFeedProps {
  onSelectDevice: (deviceId: string) => void;
  // Full-access sessions get the Raw toggle (what used to be Developer
  // Mode's separate raw-events page): the same window, unfiltered, with
  // every payload expandable as the JSON the backend actually persisted
  // (PascalCase keys and all - .NET property names serialize straight
  // through, see CaptureGallery's isTriggeredCapture note). User Mode's
  // History never shows it.
  allowRaw?: boolean;
}

// EventSeverity (Vivnest.Core.Enums) has three values - map onto the same
// status-tinted badge vocabulary the rest of the dashboard already uses,
// rather than introduce a fourth color scheme just for this list.
function badgeClassForSeverity(severity: string): string {
  if (severity === "Critical") return "icon-badge-error";
  if (severity === "Warning") return "icon-badge-warning";
  return "icon-badge-unknown";
}

function statusClassForSeverity(severity: string): string {
  if (severity === "Critical") return "status-error";
  if (severity === "Warning") return "status-warning";
  return "status-unknown";
}

export function EventsFeed({ onSelectDevice, allowRaw }: EventsFeedProps) {
  const eventsQuery = useEvents();
  const devicesQuery = useDevices();
  const [raw, setRaw] = useState(false);

  const events = useMemo(
    () => eventsQuery.data?.filter((e) => !isDuplicatedElsewhere(e)) ?? null,
    [eventsQuery.data],
  );

  const devicesById = useMemo(() => {
    const map = new Map<string, DeviceSummary>();
    for (const device of devicesQuery.data ?? []) {
      map.set(device.deviceId, device);
    }
    return map;
  }, [devicesQuery.data]);

  if (eventsQuery.isError) {
    return <ErrorState message={eventsQuery.error.message} onRetry={() => eventsQuery.refetch()} />;
  }
  if (!events) return <p>Loading events...</p>;
  if (events.length === 0 && !raw) return <p>No events yet.</p>;

  const rawToggle = allowRaw && (
    <div className="filter-chips">
      <button
        type="button"
        className={`filter-chip${raw ? " active" : ""}`}
        onClick={() => setRaw((r) => !r)}
      >
        Raw payloads
      </button>
    </div>
  );

  if (raw) {
    return (
      <>
        {rawToggle}
        <div className="entity-list">
          {(eventsQuery.data ?? []).map((event) => (
            <details
              className="entity-row entity-row-static raw-event"
              key={`${event.deviceId}|${event.eventType}|${event.occurredAtUtc}`}
            >
              <summary className="raw-event-summary">
                <span className={`status ${statusClassForSeverity(event.severity)}`}>
                  {event.severity}
                </span>
                <span className="event-type">{event.eventType}</span>
                <span className="entity-row-subtitle">
                  {devicesById.get(event.deviceId)?.name || event.deviceId} ·{" "}
                  {formatDateTimeExact(event.occurredAtUtc)}
                </span>
              </summary>
              <pre className="form-json-preview">
                {JSON.stringify(
                  {
                    deviceId: event.deviceId,
                    deviceType: event.deviceType,
                    eventType: event.eventType,
                    severity: event.severity,
                    occurredAtUtc: event.occurredAtUtc,
                    imageUrl: event.imageUrl,
                    data: event.data,
                  },
                  null,
                  2,
                )}
              </pre>
            </details>
          ))}
        </div>
      </>
    );
  }

  return (
    <>
      {rawToggle}
      <div className="entity-list">
        {events.map((event) => {
          const device = devicesById.get(event.deviceId);

          return (
            <button
              type="button"
              key={`${event.deviceId}|${event.eventType}|${event.occurredAtUtc}`}
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
    </>
  );
}
