import { useMemo, useState, useSyncExternalStore } from "react";
import { useLocation } from "wouter";
import { describeEvent, isDuplicatedElsewhere } from "./eventDescriptions";
import { formatDateTime, formatDateTimeExact } from "./format";
import { BellIcon } from "./icons";
import { useDevices, useEvents } from "./queries";

// The in-app bell (dashboard-redesign-plan.md D5) - flavor 1 from the
// parked notification-center decision, the cheap one: unread count =
// Warning/Critical events newer than a localStorage last-seen stamp,
// panel reuses the shared ["events"] query, whose 30s background
// refresh (D2) keeps the count live for free.
//
// "Read" state is localStorage BY DESIGN, not an oversight: users don't
// exist server-side (one shared tenant key), so there is nowhere to
// sync it to - see deferred-notification-center. Real push stays parked;
// it is a second delivery channel beside Telegram, not a dashboard
// feature.
const SEEN_KEY = "vivnest.alertsSeenUtc";

const ALERT_SEVERITIES = new Set(["Warning", "Critical"]);

// Two bell instances exist (header + sidebar; CSS shows one at a time),
// so the seen-stamp lives in one module-level store both subscribe to -
// otherwise opening one panel would leave the other instance's badge lit
// until its next remount.
let seenUtcCache = ((): number => {
  const stored = localStorage.getItem(SEEN_KEY);
  return stored ? Date.parse(stored) : 0;
})();

const seenListeners = new Set<() => void>();

function getSeenUtc(): number {
  return seenUtcCache;
}

function subscribeSeen(listener: () => void): () => void {
  seenListeners.add(listener);
  return () => seenListeners.delete(listener);
}

function markSeenNow(): void {
  const now = new Date().toISOString();
  localStorage.setItem(SEEN_KEY, now);
  seenUtcCache = Date.parse(now);
  for (const listener of seenListeners) listener();
}

export function NotificationBell() {
  const [, navigate] = useLocation();
  const eventsQuery = useEvents();
  const devicesQuery = useDevices();

  const [open, setOpen] = useState(false);
  const seenUtc = useSyncExternalStore(subscribeSeen, getSeenUtc);

  const alerts = useMemo(
    () =>
      (eventsQuery.data ?? []).filter(
        (e) => !isDuplicatedElsewhere(e) && ALERT_SEVERITIES.has(e.severity),
      ),
    [eventsQuery.data],
  );

  const unread = useMemo(
    () => alerts.filter((e) => Date.parse(e.occurredAtUtc) > seenUtc).length,
    [alerts, seenUtc],
  );

  const deviceNameById = useMemo(() => {
    const map = new Map<string, string>();
    for (const d of devicesQuery.data ?? []) map.set(d.deviceId, d.name || d.deviceId);
    return map;
  }, [devicesQuery.data]);

  function toggle() {
    const next = !open;
    setOpen(next);

    // Opening the panel is what "reads" the alerts - the badge clears
    // (on every instance, via the shared store), the list stays.
    if (next) markSeenNow();
  }

  return (
    <>
      <button
        type="button"
        className="notification-bell"
        onClick={toggle}
        aria-label={unread > 0 ? `Alerts (${unread} unread)` : "Alerts"}
      >
        <BellIcon className="notification-bell-icon" />
        {unread > 0 && (
          <span className="notification-badge">{unread > 9 ? "9+" : unread}</span>
        )}
      </button>

      {open && (
        <>
          <div className="notification-overlay" onClick={() => setOpen(false)} />
          <div className="notification-panel" role="dialog" aria-label="Alerts">
            <div className="notification-panel-header">Alerts</div>
            {alerts.length === 0 ? (
              <p className="form-hint">No warnings or critical events in the recent window.</p>
            ) : (
              <div className="entity-list">
                {alerts.map((event) => (
                  <button
                    type="button"
                    key={`${event.deviceId}|${event.eventType}|${event.occurredAtUtc}`}
                    className="entity-row"
                    onClick={() => {
                      setOpen(false);
                      navigate(`/devices/${encodeURIComponent(event.deviceId)}`);
                    }}
                  >
                    <div className="entity-row-main">
                      <span
                        className={`status-dot status-dot-${event.severity === "Critical" ? "error" : "warning"}`}
                      />
                      <div>
                        <div className="entity-row-title">{describeEvent(event)}</div>
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
            )}
          </div>
        </>
      )}
    </>
  );
}
