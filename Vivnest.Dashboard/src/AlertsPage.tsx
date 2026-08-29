import { useEffect, useMemo, useState } from "react";
import { isAlertEvent, markAlertsSeen, useAlertsSeenUtc } from "./alertsSeen";
import { ErrorState } from "./ErrorState";
import { describeEvent } from "./eventDescriptions";
import { formatDateTime, formatDateTimeExact } from "./format";
import { useDevices, useEvents } from "./queries";

interface AlertsPageProps {
  onSelectDevice: (deviceId: string) => void;
}

// The Alerts screen from the 2026-08-07 mockup - the bell's list as a
// full page. Same alert definition and seen-stamp (alertsSeen.ts), so
// visiting this page reads the alerts and clears the bell badge;
// anything newer than the stamp you arrived with is chipped "New" until
// you leave.
export function AlertsPage({ onSelectDevice }: AlertsPageProps) {
  const eventsQuery = useEvents();
  const devicesQuery = useDevices();
  const [severityFilter, setSeverityFilter] = useState<string | null>(null);

  // The stamp as it was when the page mounted - the live store value
  // moves the moment we mark seen below, and "New" must not vanish
  // mid-visit.
  const seenUtcNow = useAlertsSeenUtc();
  const [seenAtEntry] = useState(seenUtcNow);

  // Being on this page is what "reads" the alerts. Re-marking on every
  // data refresh keeps the badge clear for alerts that arrive while
  // you're looking straight at them (the 30s background refetch).
  const hasData = eventsQuery.data !== undefined;
  useEffect(() => {
    if (hasData) markAlertsSeen();
  }, [hasData, eventsQuery.dataUpdatedAt]);

  const alerts = useMemo(
    () => (eventsQuery.data ?? []).filter(isAlertEvent),
    [eventsQuery.data],
  );

  const severityCounts = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const a of alerts) counts[a.severity] = (counts[a.severity] ?? 0) + 1;
    return counts;
  }, [alerts]);

  const filtered = useMemo(
    () => alerts.filter((a) => !severityFilter || a.severity === severityFilter),
    [alerts, severityFilter],
  );

  const deviceNameById = useMemo(() => {
    const map = new Map<string, string>();
    for (const d of devicesQuery.data ?? []) map.set(d.deviceId, d.name || d.deviceId);
    return map;
  }, [devicesQuery.data]);

  if (eventsQuery.isError) {
    return <ErrorState message={eventsQuery.error.message} onRetry={() => eventsQuery.refetch()} />;
  }
  if (!eventsQuery.data) return <p>Loading alerts...</p>;
  if (alerts.length === 0) {
    return <p>No warnings or critical events in the recent window. All quiet.</p>;
  }

  return (
    <>
      <div className="filter-chips">
        <button
          type="button"
          className={`filter-chip${severityFilter === null ? " active" : ""}`}
          onClick={() => setSeverityFilter(null)}
        >
          All <span className="filter-chip-count">{alerts.length}</span>
        </button>
        {Object.entries(severityCounts).map(([severity, count]) => (
          <button
            type="button"
            key={severity}
            className={`filter-chip${severityFilter === severity ? " active" : ""}`}
            onClick={() => setSeverityFilter(severityFilter === severity ? null : severity)}
          >
            {severity} <span className="filter-chip-count">{count}</span>
          </button>
        ))}
      </div>

      <div className="entity-list">
        {filtered.map((event) => (
          <button
            type="button"
            key={`${event.deviceId}|${event.eventType}|${event.occurredAtUtc}`}
            className="entity-row"
            onClick={() => onSelectDevice(event.deviceId)}
          >
            <div className="entity-row-main">
              <span
                className={`status-dot status-dot-${event.severity === "Critical" ? "error" : "warning"}`}
              />
              <div>
                <div className="entity-row-title">
                  {describeEvent(event)}
                  {Date.parse(event.occurredAtUtc) > seenAtEntry && (
                    <span className="alert-new-chip">New</span>
                  )}
                </div>
                <div className="entity-row-subtitle">
                  <span>{deviceNameById.get(event.deviceId) ?? event.deviceId}</span>
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
        ))}
      </div>
    </>
  );
}
