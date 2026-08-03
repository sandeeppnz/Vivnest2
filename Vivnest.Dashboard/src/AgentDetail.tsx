import { useEffect, useState } from "react";
import {
  ApiError,
  getAgent,
  getDevices,
  type AgentSummary,
  type DeviceSummary,
} from "./api";
import { formatDateTime, formatDateTimeExact, formatInterval } from "./format";
import { AgentIcon, DeviceIcon } from "./icons";

interface AgentDetailProps {
  apiKey: string;
  agentId: string;
  onBack: () => void;
  onSelectDevice: (deviceId: string) => void;
  onAuthError: () => void;
}

export function AgentDetail({
  apiKey,
  agentId,
  onBack,
  onSelectDevice,
  onAuthError,
}: AgentDetailProps) {
  const [agent, setAgent] = useState<AgentSummary | null>(null);
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

      setError(err instanceof Error ? err.message : "Failed to load agent.");
    }

    setAgent(null);
    setDevices(null);
    setError(null);

    getAgent(apiKey, agentId)
      .then((result) => !cancelled && setAgent(result))
      .catch(handleError);

    getDevices(apiKey)
      .then((result) => !cancelled && setDevices(result))
      .catch(handleError);

    return () => {
      cancelled = true;
    };
  }, [apiKey, agentId, onAuthError]);

  const agentDevices = devices?.filter((d) => d.agentId === agentId) ?? null;

  return (
    <div className="agent-detail">
      <button type="button" className="back-button" onClick={onBack}>
        &larr; Agents
      </button>

      {error && <p className="error">{error}</p>}

      {agent && (
        <>
          <div className={`detail-header accent-${agent.status.toLowerCase()}`}>
            <div className="detail-header-main">
              <AgentIcon className="device-icon" />
              <div>
                <div className="detail-header-title">{agent.agentId}</div>
                <div className="detail-header-subtitle">
                  {agent.status} · since {formatDateTime(agent.statusSinceUtc)}
                </div>
              </div>
            </div>
          </div>

          <div className="metric-grid">
            <div className="metric-cell">
              <div className="metric-cell-label">Last heartbeat</div>
              <div className="metric-cell-value" title={formatDateTimeExact(agent.lastHeartbeatUtc)}>
                {formatDateTime(agent.lastHeartbeatUtc)}
              </div>
            </div>
            <div className="metric-cell">
              <div className="metric-cell-label">Started</div>
              <div className="metric-cell-value" title={formatDateTimeExact(agent.startedUtc)}>
                {formatDateTime(agent.startedUtc)}
              </div>
            </div>
            <div className="metric-cell">
              <div className="metric-cell-label">Interval</div>
              <div className="metric-cell-value">{formatInterval(agent.heartbeatInterval)}</div>
            </div>
            <div className="metric-cell">
              <div className="metric-cell-label">Hostname</div>
              <div className="metric-cell-value">{agent.hostName}</div>
            </div>
            <div className="metric-cell">
              <div className="metric-cell-label">Firmware</div>
              <div className="metric-cell-value">{agent.firmwareVersion || "—"}</div>
            </div>
            {agent.error && (
              <div className="metric-cell">
                <div className="metric-cell-label">Error</div>
                <div className="metric-cell-value error">{agent.error}</div>
              </div>
            )}
          </div>

          <h3 className="section-heading">Devices on this agent</h3>

          {!agentDevices ? (
            <p>Loading devices...</p>
          ) : agentDevices.length === 0 ? (
            <p>No devices reporting on this agent yet.</p>
          ) : (
            <div className="entity-list">
              {agentDevices.map((device) => (
                <button
                  type="button"
                  key={device.deviceId}
                  className={`entity-row accent-${device.status.toLowerCase()}`}
                  onClick={() => onSelectDevice(device.deviceId)}
                >
                  <div className="entity-row-main">
                    <DeviceIcon deviceType={device.deviceType} className="device-icon" />
                    <div>
                      <div className="entity-row-title">{device.deviceId}</div>
                      <div className="entity-row-subtitle">
                        <span className={`status status-${device.status.toLowerCase()}`}>
                          {device.status}
                        </span>
                      </div>
                    </div>
                  </div>
                </button>
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}
