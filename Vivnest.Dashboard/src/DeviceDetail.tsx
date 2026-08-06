import { useEffect, useState } from "react";
import { ApiError, getDevice, getDevices, type DeviceEvent, type DeviceSummary } from "./api";
import { BatteryStatus } from "./BatteryStatus";
import { CaptureGallery, isTriggeredCapture } from "./CaptureGallery";
import { DeviceEventList } from "./DeviceEventList";
import { formatDateTime, formatDateTimeExact, formatInterval } from "./format";
import { DeviceIcon, LiveFeedIcon, TriggerIcon } from "./icons";

interface DeviceDetailProps {
  apiKey: string;
  deviceId: string;
  devicesOnly: boolean;
  onBack: () => void;
  onSelectAgent: (agentId: string) => void;
  onSelectDevice: (deviceId: string) => void;
  onAuthError: () => void;
}

export function DeviceDetail({
  apiKey,
  deviceId,
  devicesOnly,
  onBack,
  onSelectAgent,
  onSelectDevice,
  onAuthError,
}: DeviceDetailProps) {
  const [device, setDevice] = useState<DeviceSummary | null>(null);
  const [devices, setDevices] = useState<DeviceSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selectedCapture, setSelectedCapture] = useState<DeviceEvent | null>(null);

  useEffect(() => {
    let cancelled = false;

    function handleError(err: unknown) {
      if (cancelled) return;

      if (err instanceof ApiError && err.status === 401) {
        onAuthError();
        return;
      }

      setError(err instanceof Error ? err.message : "Failed to load device.");
    }

    setDevice(null);
    setDevices(null);
    setError(null);
    setSelectedCapture(null);

    getDevice(apiKey, deviceId)
      .then((result) => !cancelled && setDevice(result))
      .catch(handleError);

    getDevices(apiKey)
      .then((result) => !cancelled && setDevices(result))
      .catch(handleError);

    return () => {
      cancelled = true;
    };
  }, [apiKey, deviceId, onAuthError]);

  const childDevices = devices?.filter((d) => d.parentDeviceId === deviceId) ?? null;
  const parentDevice = devices?.find((d) => d.deviceId === device?.parentDeviceId) ?? null;

  return (
    <div className="device-detail">
      <button type="button" className="back-button" onClick={onBack}>
        &larr; Devices
      </button>

      {error && <p className="error">{error}</p>}

      {device && (
        <>
          <div className="detail-header">
            <div className="detail-header-main">
              <span className={`icon-badge icon-badge-${device.status.toLowerCase()}`}>
                <DeviceIcon deviceType={device.deviceType} className="device-icon" />
              </span>
              <div>
                <div className="detail-header-title">{device.name || device.deviceId}</div>
                {device.name && <div className="detail-header-id">{device.deviceId}</div>}
                <div className="detail-header-subtitle">
                  <span className={`status-dot status-dot-${device.status.toLowerCase()}`} />
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
            {device.parentDeviceId && (
              <div className="metric-cell">
                <div className="metric-cell-label">Hub</div>
                <button
                  type="button"
                  className="metric-cell-link"
                  onClick={() => onSelectDevice(device.parentDeviceId!)}
                >
                  {parentDevice?.name || device.parentDeviceId}
                </button>
              </div>
            )}
            {device.error && (
              <div className="metric-cell">
                <div className="metric-cell-label">Error</div>
                <div className="metric-cell-value error">{device.error}</div>
              </div>
            )}
          </div>

          {childDevices && childDevices.length > 0 && (
            <>
              <h3 className="section-heading">Connected devices</h3>
              <div className="entity-list">
                {childDevices.map((child) => (
                  <button
                    type="button"
                    key={child.deviceId}
                    className="entity-row"
                    onClick={() => onSelectDevice(child.deviceId)}
                  >
                    <div className="entity-row-main">
                      <span className={`icon-badge icon-badge-${child.status.toLowerCase()}`}>
                        <DeviceIcon deviceType={child.deviceType} className="device-icon" />
                      </span>
                      <div>
                        <div className="entity-row-title">{child.name || child.deviceId}</div>
                        <div className="entity-row-subtitle">
                          <span className={`status-dot status-dot-${child.status.toLowerCase()}`} />
                        </div>
                      </div>
                    </div>
                  </button>
                ))}
              </div>
            </>
          )}

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
                    {isTriggeredCapture(selectedCapture) && (
                      <span className="live-feed-badge live-feed-badge-trigger" title="Motion-triggered capture">
                        <TriggerIcon className="live-feed-badge-icon" />
                        Triggered
                      </span>
                    )}
                  </>
                ) : (
                  <>
                    <LiveFeedIcon className="live-feed-placeholder" />
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
                timezone={device.timezone}
                selectedCapture={selectedCapture}
                onSelectCapture={setSelectedCapture}
                onAuthError={onAuthError}
              />
            </>
          ) : (
            <>
              {device.deviceType === "MotionSensor" && (
                <BatteryStatus apiKey={apiKey} deviceId={deviceId} onAuthError={onAuthError} />
              )}
              <DeviceEventList apiKey={apiKey} deviceId={deviceId} onAuthError={onAuthError} />
            </>
          )}
        </>
      )}
    </div>
  );
}
