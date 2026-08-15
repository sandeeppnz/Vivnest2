import { useEffect, useState } from "react";
import {
  ApiError,
  executeDeviceCapability,
  getAgents,
  getDevice,
  getDevices,
  type AgentSummary,
  type DeviceEvent,
  type DeviceSummary,
} from "./api";
import { BatteryStatus } from "./BatteryStatus";
import { CapabilitiesTab } from "./CapabilitiesTab";
import { CaptureGallery, isAiPending, isTriggeredCapture } from "./CaptureGallery";
import { CommandHistory } from "./CommandHistory";
import { ConfirmDialog } from "./ConfirmDialog";
import { CopyIdButton } from "./CopyIdButton";
import { DeviceEventList } from "./DeviceEventList";
import { DeviceRow } from "./DeviceRow";
import { ErrorBanner } from "./ErrorBanner";
import { formatDateTime, formatDateTimeExact, formatInterval } from "./format";
import { AgentIcon, BotIcon, DeviceIcon, LiveFeedIcon, LocationIcon, ThumbsUpIcon, TriggerIcon } from "./icons";

// Decision-log.md ADR-077 - same lookup ProjectedConfigModal.tsx/
// AgentDetail.tsx already use, kept as this file's own small copy.
const CONFIG_STATUS_CLASS: Record<string, string> = {
  UpToDate: "status-online",
  Pending: "status-warning",
  Failed: "status-error",
  NeverPublished: "status-unknown",
  Unknown: "status-unknown",
};

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
  const [agents, setAgents] = useState<AgentSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selectedCapture, setSelectedCapture] = useState<DeviceEvent | null>(null);
  const [showDetections, setShowDetections] = useState(false);
  const [naturalSize, setNaturalSize] = useState<{ width: number; height: number } | null>(null);
  const [activeTab, setActiveTab] = useState<"overview" | "capabilities">("overview");
  const [capturing, setCapturing] = useState(false);
  const [captureMessage, setCaptureMessage] = useState<string | null>(null);
  const [captureConfirmOpen, setCaptureConfirmOpen] = useState(false);

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
    setAgents(null);
    setError(null);
    setSelectedCapture(null);
    setActiveTab("overview");

    getDevice(apiKey, deviceId)
      .then((result) => !cancelled && setDevice(result))
      .catch(handleError);

    getDevices(apiKey)
      .then((result) => !cancelled && setDevices(result))
      .catch(handleError);

    // DevicesOnly keys get 403 from /agents - skip the call entirely
    // rather than fetch-then-fail, same as how the Agents tab itself is
    // hidden for them elsewhere in the app.
    if (!devicesOnly) {
      getAgents(apiKey)
        .then((result) => !cancelled && setAgents(result))
        .catch(handleError);
    }

    return () => {
      cancelled = true;
    };
  }, [apiKey, deviceId, devicesOnly, onAuthError]);

  // Stale dimensions would misplace boxes for one frame before onLoad
  // re-fires for the new image - reset eagerly on capture change instead.
  useEffect(() => {
    setNaturalSize(null);
  }, [selectedCapture]);

  const childDevices = devices?.filter((d) => d.parentDeviceId === deviceId) ?? null;
  const agent = agents?.find((a) => a.agentId === device?.agentId) ?? null;
  const parentDevice = devices?.find((d) => d.deviceId === device?.parentDeviceId) ?? null;
  const parentAgent = agents?.find((a) => a.agentId === parentDevice?.agentId) ?? null;

  async function handleCaptureNow() {
    if (!device) return;

    setCaptureConfirmOpen(false);
    setCapturing(true);
    setCaptureMessage(null);

    try {
      await executeDeviceCapability(apiKey, device.agentId, deviceId, "ImageCapture");

      setCaptureMessage("Capture requested. A new image should appear here shortly.");
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        onAuthError();
        return;
      }

      setCaptureMessage(err instanceof Error ? err.message : "Failed to request capture.");
    } finally {
      setCapturing(false);
    }
  }

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
                <div className="detail-header-title-row">
                  <div className="detail-header-title">{device.name || device.deviceId}</div>
                  <CopyIdButton value={device.deviceId} />
                </div>
                <div className="detail-header-subtitle">
                  <span className={`status-dot status-dot-${device.status.toLowerCase()}`} />
                  {device.status}
                  {device.statusSinceUtc && ` · since ${formatDateTime(device.statusSinceUtc)}`}
                </div>
                <div className="detail-header-meta-line">
                  <span className="entity-row-agent">
                    <DeviceIcon deviceType={device.deviceType} className="detail-header-agent-icon" />
                    {device.deviceType}
                  </span>
                  {!devicesOnly && agent && (
                    <>
                      {" · "}
                      <button
                        type="button"
                        className="detail-header-agent-link"
                        onClick={() => onSelectAgent(device.agentId)}
                      >
                        <AgentIcon className="detail-header-agent-icon" />
                        {agent.name || agent.agentId}
                      </button>
                    </>
                  )}
                  {device.location && (
                    <>
                      {" · "}
                      <LocationIcon className="detail-header-agent-icon" />
                      {device.location}
                    </>
                  )}
                </div>
              </div>
            </div>
            {device.deviceType === "Camera" && !devicesOnly && (
              <div className="detail-header-side">
                <div className="detail-header-actions">
                  <button
                    type="button"
                    className="logs-button"
                    onClick={() => setCaptureConfirmOpen(true)}
                    disabled={capturing}
                  >
                    <span className="label-full">{capturing ? "Capturing…" : "Capture now"}</span>
                    <span className="label-short">{capturing ? "…" : "Capture"}</span>
                  </button>
                </div>
              </div>
            )}
          </div>

          {captureMessage && <p className="restart-message">{captureMessage}</p>}

          <ConfirmDialog
            open={captureConfirmOpen}
            message={`Capture an image now from ${device.name || deviceId}?`}
            confirmLabel="Capture"
            onConfirm={handleCaptureNow}
            onCancel={() => setCaptureConfirmOpen(false)}
          />

          {device.error && <ErrorBanner message={device.error} deviceType={device.deviceType} />}

          <div className="filter-chips">
            <button
              type="button"
              className={`filter-chip${activeTab === "overview" ? " active" : ""}`}
              onClick={() => setActiveTab("overview")}
            >
              Overview
            </button>
            <button
              type="button"
              className={`filter-chip${activeTab === "capabilities" ? " active" : ""}`}
              onClick={() => setActiveTab("capabilities")}
            >
              Capabilities
            </button>
          </div>

          {activeTab === "overview" && (
          <>
          <div className="metric-grid">
            <div className="metric-cell">
              <div className="metric-cell-label">Interval</div>
              <div className="metric-cell-value">{formatInterval(device.heartbeatInterval)}</div>
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
              <div className="metric-cell-label">Brand</div>
              <div className="metric-cell-value">{device.brand || "—"}</div>
            </div>
            <div className="metric-cell">
              <div className="metric-cell-label">Model</div>
              <div className="metric-cell-value">{device.model || "—"}</div>
            </div>
            <div className="metric-cell">
              <div className="metric-cell-label">Firmware</div>
              <div className="metric-cell-value">{device.firmware || "—"}</div>
            </div>
            {/* Decision-log.md ADR-077 - reuses ConfigurationStatus already
                on DeviceSummary (ADR-075), no separate fetch. */}
            <div className="metric-cell">
              <div className="metric-cell-label">Configuration</div>
              <div className="metric-cell-value">
                <span className={`status ${CONFIG_STATUS_CLASS[device.configurationStatus.status] ?? "status-unknown"}`}>
                  {device.configurationStatus.status}
                </span>
                {device.configurationStatus.publishedVersion != null && (
                  <span>
                    {" "}v{device.configurationStatus.appliedVersion ?? "?"}/
                    {device.configurationStatus.publishedVersion}
                  </span>
                )}
              </div>
            </div>
            {device.deviceType === "Camera" && (
              <>
                <div className="metric-cell">
                  <div className="metric-cell-label">Sink check</div>
                  <div
                    className={`metric-cell-value${device.sinkCleanlinessEnabled ? " capability-on" : " capability-off"}`}
                  >
                    {device.sinkCleanlinessEnabled ? "On" : "Off"}
                  </div>
                </div>
                <div className="metric-cell">
                  <div className="metric-cell-label">Object detection</div>
                  <div
                    className={`metric-cell-value${device.objectDetectionEnabled ? " capability-on" : " capability-off"}`}
                  >
                    {device.objectDetectionEnabled ? "On" : "Off"}
                  </div>
                </div>
              </>
            )}
          </div>

          {device.parentDeviceId && (
            <>
              <h3 className="section-heading">Hub</h3>
              {!devices ? (
                <p>Loading hub...</p>
              ) : (
                parentDevice && (
                  <div className="entity-list">
                    <DeviceRow
                      device={parentDevice}
                      agent={parentAgent}
                      onClick={() => onSelectDevice(device.parentDeviceId!)}
                    />
                  </div>
                )
              )}
            </>
          )}

          {childDevices && childDevices.length > 0 && (
            <>
              <h3 className="section-heading">Connected devices</h3>
              <div className="entity-list">
                {childDevices.map((child) => {
                  const childAgent = agents?.find((a) => a.agentId === child.agentId) ?? null;

                  return (
                    <DeviceRow
                      key={child.deviceId}
                      device={child}
                      agent={childAgent}
                      onClick={() => onSelectDevice(child.deviceId)}
                    />
                  );
                })}
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
                      onLoad={(e) => {
                        const img = e.currentTarget;
                        setNaturalSize({ width: img.naturalWidth, height: img.naturalHeight });
                      }}
                    />
                    {showDetections && selectedCapture.detectedObjects && naturalSize && (
                      <svg
                        className="live-feed-detections"
                        viewBox={`0 0 ${naturalSize.width} ${naturalSize.height}`}
                        preserveAspectRatio="xMidYMid meet"
                      >
                        {selectedCapture.detectedObjects.map((obj, index) => (
                          <g
                            key={index}
                            className={`live-feed-detection${obj.className === "person" ? " live-feed-detection-person" : ""}${obj.unusual ? " live-feed-detection-unusual" : ""}`}
                          >
                            <rect
                              x={obj.x1}
                              y={obj.y1}
                              width={obj.x2 - obj.x1}
                              height={obj.y2 - obj.y1}
                            />
                            <text x={obj.x1} y={obj.y1 - 8}>
                              {obj.className}
                            </text>
                          </g>
                        ))}
                      </svg>
                    )}
                    <span className="live-feed-badge" title={formatDateTimeExact(selectedCapture.occurredAtUtc)}>
                      {formatDateTime(selectedCapture.occurredAtUtc)}
                    </span>
                    {isTriggeredCapture(selectedCapture) && (
                      <span className="live-feed-badge live-feed-badge-trigger" title="Motion-triggered capture">
                        <TriggerIcon className="live-feed-badge-icon" />
                        Triggered
                      </span>
                    )}
                    {selectedCapture.sinkCleanlinessResult !== null && (
                      <span
                        className={`live-feed-badge live-feed-badge-sink${
                          selectedCapture.sinkCleanlinessResult
                            ? " live-feed-badge-sink-clean"
                            : " live-feed-badge-sink-dirty"
                        }`}
                      >
                        <ThumbsUpIcon className="live-feed-badge-icon live-feed-badge-sink-icon" />
                        {selectedCapture.sinkCleanlinessResult ? "Clean" : "Not clean"}
                      </span>
                    )}
                    {isAiPending(selectedCapture, device) && (
                      <span className="live-feed-badge live-feed-badge-pending">
                        <BotIcon className="live-feed-badge-icon" />
                        Pending AI
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

              {(selectedCapture || device.objectDetectionEnabled) && (
                <div className="live-feed-controls">
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
                  {device.objectDetectionEnabled && (
                    <button
                      type="button"
                      className={`back-to-live-button${showDetections ? " active" : ""}`}
                      onClick={() => setShowDetections((prev) => !prev)}
                    >
                      {showDetections ? "Hide detections" : "Show detections"}
                    </button>
                  )}
                </div>
              )}

              <h3 className="section-heading">History</h3>
              <CaptureGallery
                apiKey={apiKey}
                deviceId={deviceId}
                timezone={device.timezone}
                sinkCleanlinessEnabled={device.sinkCleanlinessEnabled}
                objectDetectionEnabled={device.objectDetectionEnabled}
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

          {!devicesOnly && (
            <CommandHistory
              apiKey={apiKey}
              agentId={device.agentId}
              deviceId={deviceId}
              onAuthError={onAuthError}
            />
          )}
          </>
          )}

          {activeTab === "capabilities" && (
            <CapabilitiesTab
              apiKey={apiKey}
              deviceId={deviceId}
              onSelectDevice={onSelectDevice}
              onAuthError={onAuthError}
            />
          )}
        </>
      )}
    </div>
  );
}
