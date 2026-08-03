import { useEffect, useState } from "react";
import { ApiError, getDevices, type DeviceSummary } from "./api";
import { formatDateTime, formatDateTimeExact, formatInterval } from "./format";
import { DeviceIcon } from "./icons";

interface DeviceListProps {
  apiKey: string;
  onSelect: (deviceId: string) => void;
  onAuthError: () => void;
}

export function DeviceList({ apiKey, onSelect, onAuthError }: DeviceListProps) {
  const [devices, setDevices] = useState<DeviceSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    getDevices(apiKey)
      .then((result) => {
        if (!cancelled) setDevices(result);
      })
      .catch((err) => {
        if (cancelled) return;

        if (err instanceof ApiError && err.status === 401) {
          onAuthError();
          return;
        }

        setError(err instanceof Error ? err.message : "Failed to load devices.");
      });

    return () => {
      cancelled = true;
    };
  }, [apiKey, onAuthError]);

  if (error) return <p className="error">{error}</p>;
  if (!devices) return <p>Loading devices...</p>;
  if (devices.length === 0) return <p>No devices reporting yet.</p>;

  return (
    <div className="entity-list">
      {devices.map((device) => (
        <button
          type="button"
          key={device.deviceId}
          className={`entity-row accent-${device.status.toLowerCase()}`}
          onClick={() => onSelect(device.deviceId)}
        >
          <div className="entity-row-main">
            <DeviceIcon deviceType={device.deviceType} className="device-icon" />
            <div>
              <div className="entity-row-title">{device.deviceId}</div>
              <div className="entity-row-subtitle">
                <span className={`status status-${device.status.toLowerCase()}`}>
                  {device.status}
                </span>
                <span>{device.deviceType}</span>
              </div>
            </div>
          </div>
          <div className="entity-row-meta">
            <span title={formatDateTimeExact(device.lastHeartbeatUtc)}>
              {formatDateTime(device.lastHeartbeatUtc)}
            </span>
            <span>checks in every {formatInterval(device.heartbeatInterval)}</span>
          </div>
        </button>
      ))}
    </div>
  );
}
