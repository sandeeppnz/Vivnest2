import { useMemo } from "react";
import { ErrorState } from "./ErrorState";
import { describeEvent, isDuplicatedElsewhere } from "./eventDescriptions";
import { formatDateTime, formatDateTimeExact } from "./format";
import { useDeviceEvents } from "./queries";

interface DeviceEventListProps {
  deviceId: string;
}

export function DeviceEventList({ deviceId }: DeviceEventListProps) {
  const query = useDeviceEvents(deviceId);

  const events = useMemo(
    () => query.data?.filter((event) => !isDuplicatedElsewhere(event)) ?? null,
    [query.data],
  );

  return (
    <>
      <h3 className="section-heading">Recent events</h3>
      {query.isError ? (
        <ErrorState message={query.error.message} onRetry={() => query.refetch()} />
      ) : !events ? (
        <p>Loading events...</p>
      ) : events.length === 0 ? (
        <p>No events yet.</p>
      ) : (
        <ul className="event-list">
          {events.map((event) => (
            <li key={`${event.eventType}|${event.occurredAtUtc}`}>
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
