import { useMemo } from "react";
import { type DeviceSummary } from "./api";
import { DeviceRow } from "./DeviceRow";
import { ErrorState } from "./ErrorState";
import { describeEvent, isDuplicatedElsewhere } from "./eventDescriptions";
import { formatDateTime, formatDateTimeExact } from "./format";
import { AlertIcon, CheckIcon, DeviceIcon } from "./icons";
import { useDevices, useEvents } from "./queries";
import { countByStatus, StatusFilterChips } from "./StatusFilterChips";

interface HomeOverviewProps {
  onSelectDevice: (deviceId: string) => void;
  onGoToDevices: (statusFilter: string | null) => void;
  onGoToHistory: () => void;
}

// Home Mode's landing screen (dashboard-redesign-plan.md D4): Overview's
// device-centric half, agents deliberately absent - a devicesOnly key
// gets 403 from every /agents* route, and the mockup's Home Mode shows
// agents only as a stat, not as a destination. Everything here is a
// trimmed reuse of Overview's pieces, not a new visual language.
const ATTENTION_SEVERITY: Record<string, number> = { Error: 0, Offline: 1, Degraded: 2 };

function bySeverity(a: { status: string }, b: { status: string }): number {
  return (ATTENTION_SEVERITY[a.status] ?? 99) - (ATTENTION_SEVERITY[b.status] ?? 99);
}

function severityBadgeClass(severity: string): string {
  if (severity === "Critical") return "icon-badge-error";
  if (severity === "Warning") return "icon-badge-warning";
  return "icon-badge-unknown";
}

export function HomeOverview({ onSelectDevice, onGoToDevices, onGoToHistory }: HomeOverviewProps) {
  const devicesQuery = useDevices();
  // Events load independently - a failure hides the activity section
  // rather than blanking the whole screen.
  const eventsQuery = useEvents();

  const devices = devicesQuery.data ?? null;

  const events = useMemo(
    () => (eventsQuery.data ? eventsQuery.data.filter((e) => !isDuplicatedElsewhere(e)) : null),
    [eventsQuery.data],
  );

  const deviceCounts = useMemo(() => countByStatus(devices), [devices]);

  const attentionDevices = useMemo(
    () => (devices ?? []).filter((d) => d.status in ATTENTION_SEVERITY).sort(bySeverity),
    [devices],
  );

  const devicesById = useMemo(() => {
    const map = new Map<string, DeviceSummary>();
    for (const device of devices ?? []) {
      map.set(device.deviceId, device);
    }
    return map;
  }, [devices]);

  if (devicesQuery.isError) {
    return <ErrorState message={devicesQuery.error.message} onRetry={() => devicesQuery.refetch()} />;
  }
  if (!devices) return <p>Loading...</p>;

  const hasAttention = attentionDevices.length > 0;
  const recentEvents = (events ?? []).slice(0, 6);

  const heroTone = !hasAttention
    ? "ok"
    : attentionDevices.some((d) => d.status === "Error" || d.status === "Offline")
      ? "danger"
      : "warn";

  return (
    <>
      <div className={`health-hero health-hero-${heroTone}`}>
        <span className="health-hero-icon-wrap">
          {hasAttention ? (
            <AlertIcon className="health-hero-icon" />
          ) : (
            <CheckIcon className="health-hero-icon" />
          )}
        </span>
        <div>
          <div className="health-hero-title">
            {hasAttention
              ? `${attentionDevices.length} device${attentionDevices.length === 1 ? "" : "s"} need${attentionDevices.length === 1 ? "s" : ""} attention`
              : "Everything is healthy"}
          </div>
          <div className="health-hero-subtitle">
            {hasAttention
              ? "Tap a device below for details."
              : "All your devices are operating normally."}
          </div>
        </div>
      </div>

      <div className="kpi-grid">
        <button type="button" className="kpi-tile" onClick={() => onGoToDevices(null)}>
          <span className="kpi-value">
            {deviceCounts["Healthy"] ?? 0}
            <span className="kpi-value-suffix">/ {devices.length}</span>
          </span>
          <span className="kpi-label">Devices online</span>
        </button>
        <button type="button" className="kpi-tile" onClick={onGoToHistory}>
          <span className="kpi-value">{events === null ? "—" : events.length}</span>
          <span className="kpi-label">Recent events</span>
        </button>
      </div>

      <StatusFilterChips counts={deviceCounts} selected={null} onSelect={onGoToDevices} />

      {hasAttention && (
        <>
          <h3 className="section-heading">Needs attention</h3>
          <div className="entity-list">
            {attentionDevices.map((device) => (
              <DeviceRow key={device.deviceId} device={device} onClick={() => onSelectDevice(device.deviceId)} />
            ))}
          </div>
        </>
      )}

      {recentEvents.length > 0 && (
        <>
          <div className="section-heading-row">
            <h3 className="section-heading">Recent activity</h3>
            <button type="button" className="link-button" onClick={onGoToHistory}>
              View all &rarr;
            </button>
          </div>
          <div className="entity-list">
            {recentEvents.map((event) => {
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
                      <span className={`icon-badge ${severityBadgeClass(event.severity)}`}>
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
      )}
    </>
  );
}
