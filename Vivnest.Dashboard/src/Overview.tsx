import { useEffect, useMemo, useState } from "react";
import { ApiError, getAgents, getDevices, type AgentSummary, type DeviceSummary } from "./api";
import { AgentRow } from "./AgentRow";
import { DeviceRow } from "./DeviceRow";
import { countByStatus, StatusFilterChips } from "./StatusFilterChips";

interface OverviewProps {
  apiKey: string;
  onSelectAgent: (agentId: string) => void;
  onSelectDevice: (deviceId: string) => void;
  onGoToAgents: (statusFilter: string | null) => void;
  onGoToDevices: (statusFilter: string | null) => void;
  onAuthError: () => void;
}

// Statuses worth surfacing without being asked - Unknown just means "hasn't
// proven itself yet" (e.g. right after an agent restart), not a problem.
const ATTENTION_SEVERITY: Record<string, number> = { Error: 0, Offline: 1, Warning: 2 };

function bySeverity(a: { status: string }, b: { status: string }): number {
  return (ATTENTION_SEVERITY[a.status] ?? 99) - (ATTENTION_SEVERITY[b.status] ?? 99);
}

export function Overview({
  apiKey,
  onSelectAgent,
  onSelectDevice,
  onGoToAgents,
  onGoToDevices,
  onAuthError,
}: OverviewProps) {
  const [agents, setAgents] = useState<AgentSummary[] | null>(null);
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

      setError(err instanceof Error ? err.message : "Failed to load overview.");
    }

    getAgents(apiKey)
      .then((result) => !cancelled && setAgents(result))
      .catch(handleError);

    getDevices(apiKey)
      .then((result) => !cancelled && setDevices(result))
      .catch(handleError);

    return () => {
      cancelled = true;
    };
  }, [apiKey, onAuthError]);

  const agentCounts = useMemo(() => countByStatus(agents), [agents]);
  const deviceCounts = useMemo(() => countByStatus(devices), [devices]);

  const attentionAgents = useMemo(
    () => (agents ?? []).filter((a) => a.status in ATTENTION_SEVERITY).sort(bySeverity),
    [agents],
  );
  const attentionDevices = useMemo(
    () => (devices ?? []).filter((d) => d.status in ATTENTION_SEVERITY).sort(bySeverity),
    [devices],
  );

  if (error) return <p className="error">{error}</p>;
  if (!agents || !devices) return <p>Loading overview...</p>;

  const hasAttention = attentionAgents.length > 0 || attentionDevices.length > 0;

  return (
    <>
      <div className="overview-grid">
        <div className="overview-card">
          <button type="button" className="overview-card-header" onClick={() => onGoToAgents(null)}>
            <span className="overview-card-value">{agents.length}</span>
            <span className="overview-card-label">Agents</span>
          </button>
          <StatusFilterChips counts={agentCounts} selected={null} onSelect={onGoToAgents} />
        </div>
        <div className="overview-card">
          <button type="button" className="overview-card-header" onClick={() => onGoToDevices(null)}>
            <span className="overview-card-value">{devices.length}</span>
            <span className="overview-card-label">Devices</span>
          </button>
          <StatusFilterChips counts={deviceCounts} selected={null} onSelect={onGoToDevices} />
        </div>
      </div>

      <h3 className="section-heading">Needs attention</h3>

      {!hasAttention ? (
        <p>Everything&apos;s reporting normally.</p>
      ) : (
        <div className="entity-list">
          {attentionAgents.map((agent) => (
            <AgentRow key={agent.agentId} agent={agent} onClick={() => onSelectAgent(agent.agentId)} />
          ))}
          {attentionDevices.map((device) => (
            <DeviceRow key={device.deviceId} device={device} onClick={() => onSelectDevice(device.deviceId)} />
          ))}
        </div>
      )}
    </>
  );
}
