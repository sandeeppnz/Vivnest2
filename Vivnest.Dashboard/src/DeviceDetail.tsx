import { useEffect, useState } from "react";
import { ApiError, getDevice, type DeviceEvent, type DeviceSummary } from "./api";
import { CaptureGallery } from "./CaptureGallery";
import { DeviceEventList } from "./DeviceEventList";
import { deviceIcon, formatDateTime, formatDateTimeExact, formatInterval } from "./format";

interface DeviceDetailProps {
  apiKey: string;
  deviceId: string;
  devicesOnly: boolean;
  onBack: () => void;
  onSelectAgent: (agentId: string) => void;
  onAuthError: () => void;
}

export function DeviceDetail({
  apiKey,
  deviceId,
  devicesOnly,
  onBack,
  onSelectAgent,
  onAuthError,
}: DeviceDetailProps) {
  const [device, setDevice] = useState<DeviceSummary | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selectedCapture, setSelectedCapture] = useState<DeviceEvent | null>(null);

  useEffect(() => {
    let cancelled = false;

    setDevice(null);
    setError(null);
    setSelectedCapture(null);

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
      <button type="button" className="back-button" onClick={onBack}>
        &larr; Devices
      </button>

      {error && <p className="error">{error}</p>}

      {device && (
        <>
          <div className={`detail-header accent-${device.status.toLowerCase()}`}>
            <div className="detail-header-main">
              <span aria-hidden="true">{deviceIcon(device.deviceType)}</span>
              <div>
                <div className="detail-header-title">{device.deviceId}</div>
                <div className="detail-header-subtitle">
                  {device.status}
                  {device.statusSinceUtc && ` · since ${formatDateTime(device.statusSinceUtc)}`}
                </div>
              </div>
            </div>
          </div>

          <div className="metric-grid">
            <div className="metric-cell">
              <div className="metric-cell-label">Last heartbeat</div>
              <div className="metric-cell-value" title={formatDateTimeExact(device.lastHeartbeatUtc)}>
                {formatDateTime(device.lastHeartbeatUtc)}
              </div>
            </div>
            <div className="metric-cell">
              <div className="metric-cell-label">Last activity</div>
              <div
                className="metric-cell-value"
                title={device.lastActivityUtc ? formatDateTimeExact(device.lastActivityUtc) : undefined}
              >
                {device.lastActivityUtc ? formatDateTime(device.lastActivityUtc) : "—"}
              </div>
            </div>
            <div className="metric-cell">
              <div className="metric-cell-label">Interval</div>
              <div className="metric-cell-value">{formatInterval(device.heartbeatInterval)}</div>
            </div>
            <div className="metric-cell">
              <div className="metric-cell-label">Agent</div>
              {devicesOnly ? (
                <div className="metric-cell-value">{device.agentId}</div>
              ) : (
                <button
                  type="button"
                  className="metric-cell-link"
                  onClick={() => onSelectAgent(device.agentId)}
                >
                  {device.agentId}
                </button>
              )}
            </div>
            <div className="metric-cell">
              <div className="metric-cell-label">Tenant</div>
              <div className="metric-cell-value">{device.tenantId}</div>
            </div>
            <div className="metric-cell">
              <div className="metric-cell-label">Site</div>
              <div className="metric-cell-value">{device.siteId}</div>
            </div>
            {device.error && (
              <div className="metric-cell">
                <div className="metric-cell-label">Error</div>
                <div className="metric-cell-value error">{device.error}</div>
              </div>
            )}
          </div>

          {device.deviceType === "Camera" ? (
            <>
              <div className="live-feed">
                {selectedCapture?.imageUrl ? (
                  <>
                    <img
                      src={selectedCapture.imageUrl}
                      alt={`Capture from ${deviceId} at ${selectedCapture.occurredAtUtc}`}
                    />
                    <span className="live-feed-badge" title={formatDateTimeExact(selectedCapture.occurredAtUtc)}>
                      {formatDateTime(selectedCapture.occurredAtUtc)}
                    </span>
                  </>
                ) : (
                  <>
                    <span className="live-feed-placeholder" aria-hidden="true">📹</span>
                    <span className="live-feed-badge">
                      <span className="live-feed-badge-dot" />
                      Live
                    </span>
                  </>
                )}
              </div>

              {selectedCapture && (
                <button
                  type="button"
                  className="back-to-live-button"
                  onClick={() => setSelectedCapture(null)}
                >
                  <span className="live-feed-badge-dot" />
                  Back to live
                </button>
              )}

              <h3 className="section-heading">History</h3>
              <CaptureGallery
                apiKey={apiKey}
                deviceId={deviceId}
                selectedCapture={selectedCapture}
                onSelectCapture={setSelectedCapture}
                onAuthError={onAuthError}
              />
            </>
          ) : (
            <DeviceEventList apiKey={apiKey} deviceId={deviceId} onAuthError={onAuthError} />
          )}
        </>
      )}
    </div>
  );
}
