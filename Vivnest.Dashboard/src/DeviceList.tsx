import { useEffect, useState } from "react";
import { ApiError, getAgents, getDevices, type AgentSummary, type DeviceSummary } from "./api";
import { AgentIcon, DeviceIcon, LocationIcon } from "./icons";

interface DeviceListProps {
  apiKey: string;
  devicesOnly: boolean;
  onSelect: (deviceId: string) => void;
  onAuthError: () => void;
}

export function DeviceList({ apiKey, devicesOnly, onSelect, onAuthError }: DeviceListProps) {
  const [devices, setDevices] = useState<DeviceSummary[] | null>(null);
  const [agents, setAgents] = useState<AgentSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    function handleError(err: unknown) {
      if (cancelled) return;

      if (err instanceof ApiError && err.status === 401) {
        onAuthError();
        return;
      }

      setError(err instanceof Error ? err.message : "Failed to load devices.");
    }

    getDevices(apiKey)
      .then((result) => !cancelled && setDevices(result))
      .catch(handleError);

    // DevicesOnly keys get 403 from /agents - skip the call entirely
    // rather than fetch-then-fail, same as DeviceDetail's header.
    if (!devicesOnly) {
      getAgents(apiKey)
        .then((result) => !cancelled && setAgents(result))
        .catch(handleError);
    }

    return () => {
      cancelled = true;
    };
  }, [apiKey, devicesOnly, onAuthError]);

  if (error) return <p className="error">{error}</p>;
  if (!devices) return <p>Loading devices...</p>;
  if (devices.length === 0) return <p>No devices reporting yet.</p>;

  return (
    <div className="entity-list">
      {devices.map((device) => {
        const agent = agents?.find((a) => a.agentId === device.agentId) ?? null;

        return (
          <button
            type="button"
            key={device.deviceId}
            className="entity-row"
            onClick={() => onSelect(device.deviceId)}
          >
            <div className="entity-row-main">
              <span className={`icon-badge icon-badge-${device.status.toLowerCase()}`}>
                <DeviceIcon deviceType={device.deviceType} className="device-icon" />
              </span>
              <div>
                <div className="entity-row-title">{device.name || device.deviceId}</div>
                <div className="entity-row-subtitle">
                  <span className={`status-dot status-dot-${device.status.toLowerCase()}`} />
                  <span>{device.deviceType}</span>
                  {agent && (
                    <span className="entity-row-agent">
                      {" · "}
                      <AgentIcon className="detail-header-agent-icon" />
                      {agent.name || agent.agentId}
                    </span>
                  )}
                  {device.location && (
                    <span className="entity-row-agent">
                      {" · "}
                      <LocationIcon className="detail-header-agent-icon" />
                      {device.location}
                    </span>
                  )}
                </div>
              </div>
            </div>
          </button>
        );
      })}
    </div>
  );
}
