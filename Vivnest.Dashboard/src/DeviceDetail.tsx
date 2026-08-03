import { useEffect, useState } from "react";
import { ApiError, getDevice, type DeviceSummary } from "./api";
import { CaptureGallery } from "./CaptureGallery";
import { DeviceEventList } from "./DeviceEventList";

interface DeviceDetailProps {
  apiKey: string;
  deviceId: string;
  onBack: () => void;
  onAuthError: () => void;
}

export function DeviceDetail({ apiKey, deviceId, onBack, onAuthError }: DeviceDetailProps) {
  const [device, setDevice] = useState<DeviceSummary | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    getDevice(apiKey, deviceId)
      .then((result) => !cancelled && setDevice(result))
      .catch((err) => {
        if (cancelled) return;

        if (err instanceof ApiError && err.status === 401) {
          onAuthError();
          return;
        }

        setError(err instanceof Error ? err.message : "Failed to load device.");
      });

    return () => {
      cancelled = true;
    };
  }, [apiKey, deviceId, onAuthError]);

  return (
    <div className="device-detail">
      <button onClick={onBack}>&larr; Back to devices</button>
      <h2>{deviceId}</h2>

      {error && <p className="error">{error}</p>}

      {device && (
        <dl className="device-summary">
          <dt>Status</dt>
          <dd>
            <span className={`status status-${device.status.toLowerCase()}`}>
              {device.status}
            </span>
          </dd>
          <dt>Type</dt>
          <dd>{device.deviceType}</dd>
          <dt>Last heartbeat</dt>
          <dd>{new Date(device.lastHeartbeatUtc).toLocaleString()}</dd>
          <dt>Last activity</dt>
          <dd>{device.lastActivityUtc ? new Date(device.lastActivityUtc).toLocaleString() : "—"}</dd>
          {device.error && (
            <>
              <dt>Error</dt>
              <dd className="error">{device.error}</dd>
            </>
          )}
        </dl>
      )}

      {device?.deviceType === "Camera" ? (
        <>
          <h3>Captures</h3>
          <CaptureGallery apiKey={apiKey} deviceId={deviceId} onAuthError={onAuthError} />
        </>
      ) : (
        device && (
          <DeviceEventList apiKey={apiKey} deviceId={deviceId} onAuthError={onAuthError} />
        )
      )}
    </div>
  );
}
