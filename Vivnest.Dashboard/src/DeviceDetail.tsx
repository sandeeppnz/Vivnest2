import { useEffect, useState } from "react";
import { executeDeviceCapability, resolveCapabilityIdByKey, type DeviceEvent } from "./api";
import { ErrorState } from "./ErrorState";
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
import { DeviceConfigurationPanel } from "./DeviceConfigurationPanel";
import { useAgents, useDevice, useDeviceRegistryList, useDevices } from "./queries";
import type { DetailTab } from "./routes";
import { useApiKey } from "./session";
import { AgentIcon, BotIcon, DeviceIcon, LocationIcon, ThumbsUpIcon, TriggerIcon } from "./icons";

// Decision-log.md ADR-077 - same lookup ProjectedConfigModal.tsx/
// AgentDetail.tsx already use, kept as this file's own small copy.
const CONFIG_STATUS_CLASS: Record<string, string> = {
  UpToDate: "status-online",
  Pending: "status-warning",
  Failed: "status-error",
  NeverPublished: "status-unknown",
  Unknown: "status-unknown",
};

// "since 3 hours ago" is diagnostic when something's wrong, trivia when
// healthy (and reads as "was it broken 2 hours ago?") - the mockups show
// bare "Healthy" but "Error · since 3 min ago", so only attention states
// carry the duration.
const SHOW_STATUS_SINCE = new Set(["Error", "Offline", "Degraded"]);

interface DeviceDetailProps {
  deviceId: string;
  devicesOnly: boolean;
  // Gates the embedded publish/rollback panel (same boundary as
  // AgentDetail's actions). Capture now stays for everyone - it's a
  // user-facing feature, not administration.
  developer: boolean;
  // The tab lives in the URL since D1 (/devices/:id/:tab) so a specific
  // tab is linkable; App owns the navigation.
  activeTab: DetailTab;
  onSelectTab: (tab: DetailTab) => void;
  onBack: () => void;
  onSelectAgent: (agentId: string) => void;
  onSelectDevice: (deviceId: string) => void;
}

export function DeviceDetail({
  deviceId,
  devicesOnly,
  developer,
  activeTab,
  onSelectTab,
  onBack,
  onSelectAgent,
  onSelectDevice,
}: DeviceDetailProps) {
  const apiKey = useApiKey();
  const deviceQuery = useDevice(deviceId);
  const devicesQuery = useDevices();
  // DevicesOnly keys get 403 from /agents - skip the query entirely,
  // same as how the Agents tab itself is hidden for them.
  const agentsQuery = useAgents(!devicesOnly);

  const device = deviceQuery.data ?? null;
  const devices = devicesQuery.data ?? null;
  const agents = agentsQuery.data ?? null;

  // The projected-config panel is keyed by the ADMIN registry id; this
  // runtime device maps to it via DeviceRegistry.runtimeDeviceId
  // (decision-log.md ADR-063). No registry link, no panel - the admin
  // registry browser is where the link gets made. The panel is
  // Developer-only (and devicesOnly keys get 403 from the registry
  // route anyway), so the lookup is skipped otherwise.
  const registryQuery = useDeviceRegistryList(developer);
  const registryEntry = registryQuery.data?.find((r) => r.runtimeDeviceId === deviceId) ?? null;

  const [selectedCapture, setSelectedCapture] = useState<DeviceEvent | null>(null);
  const [showDetections, setShowDetections] = useState(false);
  const [naturalSize, setNaturalSize] = useState<{ width: number; height: number } | null>(null);
  const [capturing, setCapturing] = useState(false);
  const [captureMessage, setCaptureMessage] = useState<string | null>(null);
  const [captureConfirmOpen, setCaptureConfirmOpen] = useState(false);

  // The queries survive a device switch (they're cache entries), but the
  // capture selection is view state for ONE device.
  useEffect(() => {
    setSelectedCapture(null);
  }, [deviceId]);

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
      // ADR-104 (Command Routing 1.9.9) - send the catalogue capabilityId,
      // not the legacy "ImageCapture" literal.
      //
      // The literal still works: CommandDispatcher special-cases it into a
      // built-in ownership check that skips the DeviceCapability lookup
      // entirely. That short-circuit is exactly why the identity bug
      // ADR-104 fixes went unnoticed for so long - it meant the real
      // assignment path was never executed by the only caller there is.
      // Sending the id puts this button on the validated path.
      const capabilityId = await resolveCapabilityIdByKey(apiKey, "camera.capture");

      await executeDeviceCapability(apiKey, device.agentId, deviceId, capabilityId);

      setCaptureMessage("Capture requested. A new image should appear here shortly.");
    } catch (err) {
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

      {deviceQuery.isError && (
        <ErrorState message={deviceQuery.error.message} onRetry={() => deviceQuery.refetch()} />
      )}

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
                  {device.statusSinceUtc &&
                    SHOW_STATUS_SINCE.has(device.status) &&
                    ` · since ${formatDateTime(device.statusSinceUtc)}`}
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
          </div>

          <ConfirmDialog
            open={captureConfirmOpen}
            message={`Capture an image now from ${device.name || deviceId}?`}
            confirmLabel="Capture"
            onConfirm={handleCaptureNow}
            onCancel={() => setCaptureConfirmOpen(false)}
          />

          {device.error && <ErrorBanner message={device.error} deviceType={device.deviceType} />}

          <div className="detail-tabs">
            <button
              type="button"
              className={`detail-tab${activeTab === "overview" ? " active" : ""}`}
              onClick={() => onSelectTab("overview")}
            >
              Overview
            </button>
            {/* A devicesOnly key can't see command history, and cameras have
                no event list (captures live on Overview) - so for that
                combination the tab would be empty; hide it instead. */}
            {(device.deviceType !== "Camera" || !devicesOnly) && (
              <button
                type="button"
                className={`detail-tab${activeTab === "activity" ? " active" : ""}`}
                onClick={() => onSelectTab("activity")}
              >
                Activity
              </button>
            )}
            <button
              type="button"
              className={`detail-tab${activeTab === "configuration" ? " active" : ""}`}
              onClick={() => onSelectTab("configuration")}
            >
              Configuration
            </button>
          </div>

          {activeTab === "overview" && (
          <>
          {/* Heartbeat trio (per the camera-detail mockup): last heartbeat
              next to the expected interval tells you whether the device is
              late; last activity is the last real sign of life. The old
              Sink check / Object detection On/Off cells were config flags,
              shown better on the Configuration tab (Enabled + operational
              status + settings), so they don't repeat here. */}
          <div className="metric-grid">
            <div className="metric-cell">
              <div className="metric-cell-label">Last heartbeat</div>
              <div
                className="metric-cell-value"
                title={formatDateTimeExact(device.lastHeartbeatUtc)}
              >
                {formatDateTime(device.lastHeartbeatUtc)}
              </div>
            </div>
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
              {/* Capture-preview hero - the latest capture by default, or
                  whichever gallery capture is selected. Deliberately no
                  "Live" placeholder: no streaming pipeline exists (ADR-018),
                  so nothing here should imply one. No captures at all means
                  no panel. */}
              {(selectedCapture?.imageUrl || device.thumbnailUrl) && (
              <div className="live-feed">
                {selectedCapture?.imageUrl ? (
                  <>
                    <img
                      // Keyed per capture so React remounts the element even
                      // when the URL matches the latest-capture thumbnail
                      // (common: the thumbnail IS the latest capture) -
                      // otherwise onLoad never re-fires and naturalSize stays
                      // null, which would keep the detection overlay hidden.
                      key={selectedCapture.occurredAtUtc}
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
                    <img key="latest" src={device.thumbnailUrl!} alt={`Latest capture from ${deviceId}`} />
                    {/* Same badge slot as the selected-capture timestamp:
                        the badge always says when the shown image was taken.
                        For a camera, lastActivityUtc is stamped at capture
                        time on the agent - the closest thing to the
                        thumbnail's own timestamp without a new API field. */}
                    <span
                      className="live-feed-badge"
                      title={device.lastActivityUtc ? formatDateTimeExact(device.lastActivityUtc) : undefined}
                    >
                      {device.lastActivityUtc ? formatDateTime(device.lastActivityUtc) : "Latest capture"}
                    </span>
                  </>
                )}
              </div>
              )}

              {/* Feed controls. Capture now moved here from the page header
                  (2026-08-29) - it acts on this feed, and it matters most
                  when no capture exists yet, which is exactly when the hero
                  above doesn't render at all. Hidden for devicesOnly keys,
                  whose commands the server refuses. The selected-capture
                  pair only makes sense while a specific capture is shown -
                  the detections toggle draws on that capture's boxes. */}
              {(!devicesOnly || selectedCapture) && (
                <div className="live-feed-controls">
                  {!devicesOnly && (
                    <button
                      type="button"
                      className="back-to-live-button"
                      onClick={() => setCaptureConfirmOpen(true)}
                      disabled={capturing}
                    >
                      {capturing ? "Capturing…" : "Capture now"}
                    </button>
                  )}
                  {selectedCapture && (
                    <>
                      <button
                        type="button"
                        className="back-to-live-button"
                        onClick={() => setSelectedCapture(null)}
                      >
                        Back to latest
                      </button>
                      {device.objectDetectionEnabled && (
                        <button
                          type="button"
                          className={`back-to-live-button${showDetections ? " active" : ""}`}
                          onClick={() => setShowDetections((prev) => !prev)}
                        >
                          {showDetections ? "Hide detections" : "Show detections"}
                        </button>
                      )}
                    </>
                  )}
                </div>
              )}
              {captureMessage && <p className="restart-message">{captureMessage}</p>}

              <h3 className="section-heading">History</h3>
              <CaptureGallery
                deviceId={deviceId}
                timezone={device.timezone}
                sinkCleanlinessEnabled={device.sinkCleanlinessEnabled}
                objectDetectionEnabled={device.objectDetectionEnabled}
                selectedCapture={selectedCapture}
                onSelectCapture={setSelectedCapture}
              />
            </>
          ) : (
            device.deviceType === "MotionSensor" && (
              <BatteryStatus deviceId={deviceId} />
            )
          )}

          {/* Moved from the Configuration tab (2026-08-29), same as
              AgentDetail's Agent info: identity facts, not configuration.
              Reference data, so it sits last. */}
          <h3 className="section-heading">Device info</h3>
          <div className="metric-grid">
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
            <div className="metric-cell">
              <div className="metric-cell-label">Timezone</div>
              <div className="metric-cell-value">{device.timezone || "—"}</div>
            </div>
          </div>
          {/* System metrics (CPU/memory/upload) are agent-level in
              Vivnest - point there instead of a thin per-device System
              Info tab with nothing real to show. Hidden for devicesOnly
              keys, which get 403 from /agents*. */}
          {!devicesOnly && agent && (
            <p className="form-hint">
              System metrics (CPU, memory, upload) are reported by the owning agent.{" "}
              <button
                type="button"
                className="link-button"
                onClick={() => onSelectAgent(device.agentId)}
              >
                View {agent.name || agent.agentId} &rarr;
              </button>
            </p>
          )}
          </>
          )}

          {activeTab === "activity" && (
            <>
              {/* Cameras deliberately have no event list - every camera
                  event is a capture, shown richer in Overview's gallery. */}
              {device.deviceType !== "Camera" && (
                <DeviceEventList deviceId={deviceId} />
              )}
              {!devicesOnly && (
                <CommandHistory agentId={device.agentId} deviceId={deviceId} />
              )}
            </>
          )}

          {activeTab === "configuration" && (
            <>
              {/* Nothing but configurable state here: sync status,
                  capabilities, and the publish panel. Identity facts live
                  on Overview's Device info section. */}
              <div className="metric-grid">
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
              </div>
              <CapabilitiesTab deviceId={deviceId} onSelectDevice={onSelectDevice} />
              {/* Publish/Rollback live inside this panel - Developer only,
                  same boundary as AgentDetail's actions. */}
              {developer && registryEntry && (
                <>
                  <h3 className="section-heading">Published configuration</h3>
                  <div className="config-section">
                    <DeviceConfigurationPanel registryDeviceId={registryEntry.deviceId} />
                  </div>
                </>
              )}
            </>
          )}
        </>
      )}
    </div>
  );
}
