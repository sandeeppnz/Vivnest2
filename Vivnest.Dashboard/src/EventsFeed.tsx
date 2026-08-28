import { useMemo } from "react";
import { type DeviceSummary } from "./api";
import { ErrorState } from "./ErrorState";
import { describeEvent, isDuplicatedElsewhere } from "./eventDescriptions";
import { formatDateTime, formatDateTimeExact } from "./format";
import { DeviceIcon } from "./icons";
import { useDevices, useEvents } from "./queries";

interface EventsFeedProps {
  onSelectDevice: (deviceId: string) => void;
}

// EventSeverity (Vivnest.Core.Enums) has three values - map onto the same
// status-tinted badge vocabulary the rest of the dashboard already uses,
// rather than introduce a fourth color scheme just for this list.
function badgeClassForSeverity(severity: string): string {
  if (severity === "Critical") return "icon-badge-error";
  if (severity === "Warning") return "icon-badge-warning";
  return "icon-badge-unknown";
}

export function EventsFeed({ onSelectDevice }: EventsFeedProps) {
  const eventsQuery = useEvents();
  const devicesQuery = useDevices();

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
  if (events.length === 0) return <p>No events yet.</p>;

  return (
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
  );
}
