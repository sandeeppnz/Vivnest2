import { ErrorState } from "./ErrorState";
import { formatDateTimeExact } from "./format";
import { useDevices, useEvents } from "./queries";

// Developer Mode's events view: the same window the Events feed shows,
// but RAW - no isDuplicatedElsewhere filtering, no friendly
// describeEvent labels, and every payload expandable as the JSON the
// backend actually persisted (PascalCase keys and all - .NET property
// names serialize straight through, see CaptureGallery's
// isTriggeredCapture note).
function severityClass(severity: string): string {
  if (severity === "Critical") return "status-error";
  if (severity === "Warning") return "status-warning";
  return "status-unknown";
}

export function RawEventsPage() {
  const eventsQuery = useEvents();
  const devicesQuery = useDevices();

  if (eventsQuery.isError) {
    return <ErrorState message={eventsQuery.error.message} onRetry={() => eventsQuery.refetch()} />;
  }
  if (!eventsQuery.data) return <p>Loading events...</p>;
  if (eventsQuery.data.length === 0) return <p>No events in the window.</p>;

  const deviceNameById = new Map(
    (devicesQuery.data ?? []).map((d) => [d.deviceId, d.name || d.deviceId]),
  );

  return (
    <div className="entity-list">
      {eventsQuery.data.map((event) => (
        <details
          className="entity-row entity-row-static raw-event"
          key={`${event.deviceId}|${event.eventType}|${event.occurredAtUtc}`}
        >
          <summary className="raw-event-summary">
            <span className={`status ${severityClass(event.severity)}`}>{event.severity}</span>
            <span className="event-type">{event.eventType}</span>
            <span className="entity-row-subtitle">
              {deviceNameById.get(event.deviceId) ?? event.deviceId} · {formatDateTimeExact(event.occurredAtUtc)}
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
  );
}
