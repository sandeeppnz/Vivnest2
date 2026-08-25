import { useEffect, useMemo, useState } from "react";
import {
  ApiError,
  getAgents,
  getDevices,
  getEvents,
  type AgentSummary,
  type DeviceEvent,
  type DeviceSummary,
} from "./api";
import { ErrorState } from "./ErrorState";
import { AgentRow } from "./AgentRow";
import { DeviceRow } from "./DeviceRow";
import { countByStatus, StatusFilterChips } from "./StatusFilterChips";
import { describeEvent, isDuplicatedElsewhere } from "./eventDescriptions";
import { formatDateTime, formatDateTimeExact } from "./format";
import { AlertIcon, CheckIcon, DeviceIcon } from "./icons";

interface OverviewProps {
  apiKey: string;
  onSelectAgent: (agentId: string) => void;
  onSelectDevice: (deviceId: string) => void;
  onGoToAgents: (statusFilter: string | null) => void;
  onGoToDevices: (statusFilter: string | null) => void;
  onGoToEvents: () => void;
  onAuthError: () => void;
}

// Statuses worth surfacing without being asked - Unknown just means "hasn't
// proven itself yet" (e.g. right after an agent restart), not a problem.
const ATTENTION_SEVERITY: Record<string, number> = { Error: 0, Offline: 1, Degraded: 2 };

function bySeverity(a: { status: string }, b: { status: string }): number {
  return (ATTENTION_SEVERITY[a.status] ?? 99) - (ATTENTION_SEVERITY[b.status] ?? 99);
}

// Same ordering StatusFilterChips uses - live states first, NotApplicable last.
const STATUS_ORDER = ["Healthy", "Degraded", "Offline", "Error", "Unknown", "NotApplicable"];

// One thin stacked bar showing the status mix at a glance - segments are
// clickable and navigate pre-filtered, same as the chips below it.
function StatusDistributionBar({
  counts,
  onSelect,
}: {
  counts: Record<string, number>;
  onSelect: (status: string) => void;
}) {
  const total = Object.values(counts).reduce((sum, count) => sum + count, 0);

  if (total === 0) {
    return <div className="dist-bar"><div className="dist-segment dist-empty" style={{ flexGrow: 1 }} /></div>;
  }

  return (
    <div className="dist-bar">
      {STATUS_ORDER.filter((status) => counts[status] > 0).map((status) => (
        <button
          type="button"
          key={status}
          className={`dist-segment dist-${status.toLowerCase()}`}
          style={{ flexGrow: counts[status] }}
          title={`${status}: ${counts[status]}`}
          aria-label={`${status}: ${counts[status]}`}
          onClick={() => onSelect(status)}
        />
      ))}
    </div>
  );
}

// Up-to-date ratio as a meter - green ramp when everything's current,
// amber ramp while anything is pending/failed (the fill carries severity,
// the track is a lighter step of the same ramp).
function SyncMeter({ label, upToDate, total }: { label: string; upToDate: number; total: number }) {
  const complete = upToDate === total;

  return (
    <div className="sync-meter">
      <div className="sync-meter-header">
        <span className="sync-meter-label">{label}</span>
        <span className="sync-meter-value">
          {upToDate}/{total} up to date
        </span>
      </div>
      <div className={`sync-meter-track${complete ? " sync-meter-track-ok" : ""}`}>
        <div
          className={`sync-meter-fill${complete ? " sync-meter-fill-ok" : ""}`}
          style={{ width: `${total === 0 ? 0 : (upToDate / total) * 100}%` }}
        />
      </div>
    </div>
  );
}

function severityBadgeClass(severity: string): string {
  if (severity === "Critical") return "icon-badge-error";
  if (severity === "Warning") return "icon-badge-warning";
  return "icon-badge-unknown";
}

export function Overview({
  apiKey,
  onSelectAgent,
  onSelectDevice,
  onGoToAgents,
  onGoToDevices,
  onGoToEvents,
  onAuthError,
}: OverviewProps) {
  const [agents, setAgents] = useState<AgentSummary[] | null>(null);
  const [devices, setDevices] = useState<DeviceSummary[] | null>(null);
  // Events load independently - a failure here hides the activity section
  // rather than blanking the whole overview, unlike agents/devices which
  // the page can't render without.
  const [events, setEvents] = useState<DeviceEvent[] | null>(null);
  // Whether the events FETCH filled its 50-item window - measured on the
  // raw response, before isDuplicatedElsewhere filtering, since the
  // filtered list can be shorter than 50 while the true count is higher.
  const [eventsWindowFull, setEventsWindowFull] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reloadNonce, setReloadNonce] = useState(0);

  function retryLoad() {
    setError(null);
    setReloadNonce((n) => n + 1);
  }

  useEffect(() => {
    let cancelled = false;

    function handleError(err: unknown) {
      if (cancelled) return;

      if (err instanceof ApiError && err.status === 401) {
        onAuthError();
        return;
      }

      setError(err instanceof Error ? err.message : "Failed to load overview.");
    }

    getAgents(apiKey)
      .then((result) => !cancelled && setAgents(result))
      .catch(handleError);

    getDevices(apiKey)
      .then((result) => !cancelled && setDevices(result))
      .catch(handleError);

    getEvents(apiKey)
      .then((result) => {
        if (cancelled) return;
        setEventsWindowFull(result.length >= 50);
        setEvents(result.filter((e) => !isDuplicatedElsewhere(e)));
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        if (err instanceof ApiError && err.status === 401) {
          onAuthError();
        }
        // Any other failure: leave events null - section stays hidden.
      });

    return () => {
      cancelled = true;
    };
  }, [apiKey, onAuthError, reloadNonce]);

  const agentCounts = useMemo(() => countByStatus(agents), [agents]);
  const deviceCounts = useMemo(() => countByStatus(devices), [devices]);

  // Decision-log.md ADR-077 - a small rollup of the config/version status
  // already on every Agent/Device row (ADR-075), not a separate fetch.
  // "Relevant" excludes NeverPublished/NeverDeployed from the denominator -
  // an Agent/Device that's never had a config or version set isn't
  // meaningfully "out of date," it just hasn't been asked to be current.
  const configRollup = useMemo(() => {
    const relevant = (agents ?? [])
      .map((a) => a.configurationStatus.status)
      .concat((devices ?? []).map((d) => d.configurationStatus.status))
      .filter((s) => s !== "NeverPublished");

    const upToDate = relevant.filter((s) => s === "UpToDate").length;

    return { total: relevant.length, upToDate };
  }, [agents, devices]);

  const versionRollup = useMemo(() => {
    const relevant = (agents ?? [])
      .map((a) => a.versionStatus.status)
      .filter((s) => s !== "NeverDeployed");

    const upToDate = relevant.filter((s) => s === "UpToDate").length;

    return { total: relevant.length, upToDate };
  }, [agents]);

  const attentionAgents = useMemo(
    () => (agents ?? []).filter((a) => a.status in ATTENTION_SEVERITY).sort(bySeverity),
    [agents],
  );
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

  // Count within the fetched window (getEvents caps at 50) - if every
  // fetched event is inside the last 24h the true count may be higher,
  // so show "50+" rather than a wrong exact number.
  const eventsLast24h = useMemo(() => {
    if (!events) return null;
    const cutoff = Date.now() - 24 * 3600 * 1000;
    const count = events.filter((e) => new Date(e.occurredAtUtc).getTime() >= cutoff).length;
    return { count, capped: eventsWindowFull && count === events.length };
  }, [events, eventsWindowFull]);

  if (error) return <ErrorState message={error} onRetry={retryLoad} />;
  if (!agents || !devices) return <p>Loading overview...</p>;

  const attentionCount = attentionAgents.length + attentionDevices.length;
  const hasAttention = attentionCount > 0;
  const recentEvents = (events ?? []).slice(0, 6);

  // Breakdown for the hero subtitle ("1 error · 2 offline"), worst first.
  const attentionBreakdown = Object.keys(ATTENTION_SEVERITY)
    .sort((a, b) => ATTENTION_SEVERITY[a] - ATTENTION_SEVERITY[b])
    .map((status) => ({
      status,
      count: (agentCounts[status] ?? 0) + (deviceCounts[status] ?? 0),
    }))
    .filter((entry) => entry.count > 0);

  // Errors and offline read as danger; degraded-only is a caution, not an
  // emergency - same severity vocabulary ATTENTION_SEVERITY already encodes.
  const heroTone = !hasAttention
    ? "ok"
    : attentionBreakdown.some((e) => e.status === "Error" || e.status === "Offline")
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
              ? `${attentionCount} need${attentionCount === 1 ? "s" : ""} attention`
              : "Everything is healthy"}
          </div>
          <div className="health-hero-subtitle">
            {hasAttention
              ? attentionBreakdown
                  .map((e) => `${e.count} ${e.status.toLowerCase()}`)
                  .join(" · ")
              : "All agents and devices are operating normally."}
          </div>
        </div>
      </div>

      <div className="kpi-grid">
        <button type="button" className="kpi-tile" onClick={() => onGoToAgents(null)}>
          <span className="kpi-value">
            {agentCounts["Healthy"] ?? 0}
            <span className="kpi-value-suffix">/ {agents.length}</span>
          </span>
          <span className="kpi-label">Agents online</span>
        </button>
        <button type="button" className="kpi-tile" onClick={() => onGoToDevices(null)}>
          <span className="kpi-value">
            {deviceCounts["Healthy"] ?? 0}
            <span className="kpi-value-suffix">/ {devices.length}</span>
          </span>
          <span className="kpi-label">Devices online</span>
        </button>
        <button type="button" className="kpi-tile" onClick={onGoToEvents}>
          <span className="kpi-value">
            {eventsLast24h === null ? "—" : `${eventsLast24h.count}${eventsLast24h.capped ? "+" : ""}`}
          </span>
          <span className="kpi-label">Events (24h)</span>
        </button>
      </div>

      <div className="overview-grid">
        <div className="overview-card">
          <button type="button" className="overview-card-header" onClick={() => onGoToAgents(null)}>
            <span className="overview-card-value">{agents.length}</span>
            <span className="overview-card-label">Agents</span>
          </button>
          <StatusDistributionBar counts={agentCounts} onSelect={onGoToAgents} />
          <StatusFilterChips counts={agentCounts} selected={null} onSelect={onGoToAgents} />
        </div>
        <div className="overview-card">
          <button type="button" className="overview-card-header" onClick={() => onGoToDevices(null)}>
            <span className="overview-card-value">{devices.length}</span>
            <span className="overview-card-label">Devices</span>
          </button>
          <StatusDistributionBar counts={deviceCounts} onSelect={onGoToDevices} />
          <StatusFilterChips counts={deviceCounts} selected={null} onSelect={onGoToDevices} />
        </div>
      </div>

      {(configRollup.total > 0 || versionRollup.total > 0) && (
        <div className="sync-meter-grid">
          {configRollup.total > 0 && (
            <SyncMeter label="Configuration" upToDate={configRollup.upToDate} total={configRollup.total} />
          )}
          {versionRollup.total > 0 && (
            <SyncMeter label="Software" upToDate={versionRollup.upToDate} total={versionRollup.total} />
          )}
        </div>
      )}

      {/* The hero banner already says all-clear when healthy - only render
          the section when there's actually something to look at. */}
      {hasAttention && (
        <>
          <h3 className="section-heading">Needs attention</h3>
          <div className="entity-list">
            {attentionAgents.map((agent) => (
              <AgentRow key={agent.agentId} agent={agent} onClick={() => onSelectAgent(agent.agentId)} />
            ))}
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
            <button type="button" className="link-button" onClick={onGoToEvents}>
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
