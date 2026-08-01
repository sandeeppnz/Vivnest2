import { useEffect, useState } from "react";
import { ApiError, getDevices, type DeviceSummary } from "./api";

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
    <table className="device-table">
      <thead>
        <tr>
          <th>Device</th>
          <th>Type</th>
          <th>Status</th>
          <th>Last heartbeat</th>
        </tr>
      </thead>
      <tbody>
        {devices.map((device) => (
          <tr key={device.deviceId} onClick={() => onSelect(device.deviceId)}>
            <td>{device.deviceId}</td>
            <td>{device.deviceType}</td>
            <td>
              <span className={`status status-${device.status.toLowerCase()}`}>
                {device.status}
              </span>
            </td>
            <td>{new Date(device.lastHeartbeatUtc).toLocaleString()}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
