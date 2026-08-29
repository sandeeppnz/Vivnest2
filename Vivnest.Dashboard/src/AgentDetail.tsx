import { useState } from "react";
import {
  applyAgentConfiguration,
  deployAgent,
  getAgentLogs,
  refreshAgentConfiguration,
  restartAgent,
} from "./api";
import { ErrorState } from "./ErrorState";
import { formatDateTime, formatDateTimeExact, formatInterval, formatUptime } from "./format";
import { AgentConfigurationPanel } from "./AgentConfigurationPanel";
import { useAgent, useAgentMetrics, useAgentRegistryList, useDevices } from "./queries";
import type { DetailTab } from "./routes";
import { useApiKey } from "./session";
import { AgentIcon } from "./icons";
import { AgentMetricsChart } from "./AgentMetricsChart";
import { CommandHistory } from "./CommandHistory";
import { ConfirmDialog } from "./ConfirmDialog";
import { CopyIdButton } from "./CopyIdButton";
import { DeviceRow } from "./DeviceRow";
import { ErrorBanner } from "./ErrorBanner";

interface AgentDetailProps {
  agentId: string;
  // Gates every action on this page (Restart, Download logs,
  // Refresh/Apply/Deploy, and the embedded publish/rollback panel) -
  // User Mode gets the same page read-only, actions in place only for
  // Developer. Same child-proofing boundary as Settings' Admin/Debug.
  developer: boolean;
  // See DeviceDetail - the tab lives in the URL since D1.
  activeTab: DetailTab;
  onSelectTab: (tab: DetailTab) => void;
  onBack: () => void;
  onSelectDevice: (deviceId: string) => void;
}

// Decision-log.md ADR-077 - same class-per-status lookup ProjectedConfigModal.tsx
// (config) and AgentInstallationsAdmin.tsx (version) already use, kept as
// each file's own small copy rather than a shared import - consistent with
// how those two originals are already independently defined.
const CONFIG_STATUS_CLASS: Record<string, string> = {
  UpToDate: "status-online",
  Pending: "status-warning",
  Failed: "status-error",
  NeverPublished: "status-unknown",
  Unknown: "status-unknown",
};

const VERSION_STATUS_CLASS: Record<string, string> = {
  UpToDate: "status-online",
  Outdated: "status-warning",
  NeverDeployed: "status-unknown",
  Unknown: "status-unknown",
};

// Same rule as DeviceDetail's copy: the "since" duration is diagnostic for
// attention states, trivia for a healthy one - see that file's comment.
const SHOW_STATUS_SINCE = new Set(["Error", "Offline", "Degraded"]);

export function AgentDetail({
  agentId,
  developer,
  activeTab,
  onSelectTab,
  onBack,
  onSelectDevice,
}: AgentDetailProps) {
  const apiKey = useApiKey();
  const agentQuery = useAgent(agentId);
  const devicesQuery = useDevices();
  const metricsQuery = useAgentMetrics(agentId);

  const agent = agentQuery.data ?? null;
  const devices = devicesQuery.data ?? null;
  const metrics = metricsQuery.data ?? null;

  // Registry link for the projected-config panel - see DeviceDetail's
  // identical lookup (runtime id -> admin registry id, ADR-063). The
  // panel is Developer-only, so the lookup is skipped otherwise.
  const registryQuery = useAgentRegistryList(developer);
  const registryEntry = registryQuery.data?.find((r) => r.runtimeAgentId === agentId) ?? null;

  const [restarting, setRestarting] = useState(false);
  const [restartMessage, setRestartMessage] = useState<string | null>(null);
  const [downloadingLogs, setDownloadingLogs] = useState(false);
  const [logsMessage, setLogsMessage] = useState<string | null>(null);
  const [deploying, setDeploying] = useState(false);
  const [deployMessage, setDeployMessage] = useState<string | null>(null);
  const [restartConfirmOpen, setRestartConfirmOpen] = useState(false);
  const [deployConfirmOpen, setDeployConfirmOpen] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [refreshMessage, setRefreshMessage] = useState<string | null>(null);
  const [refreshConfirmOpen, setRefreshConfirmOpen] = useState(false);
  const [applying, setApplying] = useState(false);
  const [applyMessage, setApplyMessage] = useState<string | null>(null);
  const [applyInputOpen, setApplyInputOpen] = useState(false);
  const [applyVersionInput, setApplyVersionInput] = useState("");

  const agentDevices = devices?.filter((d) => d.agentId === agentId) ?? null;

  async function handleRestart() {
    setRestartConfirmOpen(false);
    setRestarting(true);
    setRestartMessage(null);

    try {
      await restartAgent(apiKey, agentId);

      setRestartMessage("Restart requested. The agent should reconnect shortly.");
    } catch (err) {
      setRestartMessage(
        err instanceof Error ? err.message : "Failed to request restart.",
      );
    } finally {
      setRestarting(false);
    }
  }

  async function handleDeploy() {
    setDeployConfirmOpen(false);
    setDeploying(true);
    setDeployMessage(null);

    try {
      await deployAgent(apiKey, agentId);

      setDeployMessage("Deploy requested. The agent should be back on the latest build shortly.");
    } catch (err) {
      setDeployMessage(
        err instanceof Error ? err.message : "Failed to request deploy.",
      );
    } finally {
      setDeploying(false);
    }
  }

  async function handleRefreshConfiguration() {
    setRefreshConfirmOpen(false);
    setRefreshing(true);
    setRefreshMessage(null);

    try {
      await refreshAgentConfiguration(apiKey, agentId);

      setRefreshMessage("Refresh requested. If a newer configuration is published, the agent will restart to adopt it.");
    } catch (err) {
      setRefreshMessage(
        err instanceof Error ? err.message : "Failed to request configuration refresh.",
      );
    } finally {
      setRefreshing(false);
    }
  }

  async function handleApplyConfiguration() {
    // Number(), not parseInt(): parseInt silently truncates "1.5" to 1 and
    // "2abc" to 2, so a typo would apply a version the user never typed.
    // Number() makes those NaN/non-integer and they get rejected instead.
    const version = Number(applyVersionInput.trim());

    if (!Number.isInteger(version) || version < 1) {
      setApplyMessage("Enter a valid configuration version number.");
      return;
    }

    setApplyInputOpen(false);
    setApplying(true);
    setApplyMessage(null);

    try {
      await applyAgentConfiguration(apiKey, agentId, version);

      setApplyMessage(`Apply requested for version ${version}. The agent will restart if that version differs from what's currently applied.`);
    } catch (err) {
      setApplyMessage(
        err instanceof Error ? err.message : "Failed to request configuration apply.",
      );
    } finally {
      setApplying(false);
      setApplyVersionInput("");
    }
  }

  async function handleDownloadLogs() {
    setDownloadingLogs(true);
    setLogsMessage(null);

    try {
      const { url } = await getAgentLogs(apiKey, agentId);

      window.open(url, "_blank");
    } catch (err) {
      setLogsMessage(
        err instanceof Error ? err.message : "Failed to fetch log download link.",
      );
    } finally {
      setDownloadingLogs(false);
    }
  }

  return (
    <div className="agent-detail">
      <button type="button" className="back-button" onClick={onBack}>
        &larr; Agents
      </button>

      {agentQuery.isError && (
        <ErrorState message={agentQuery.error.message} onRetry={() => agentQuery.refetch()} />
      )}

      {agent && (
        <>
          <div className="detail-header">
            <div className="detail-header-main">
              <span className={`icon-badge icon-badge-${agent.status.toLowerCase()}`}>
                <AgentIcon className="device-icon" />
              </span>
              <div>
                <div className="detail-header-title-row">
                  <div className="detail-header-title">{agent.name || agent.agentId}</div>
                  <CopyIdButton value={agent.agentId} />
                </div>
                <div className="detail-header-subtitle">
                  <span className={`status-dot status-dot-${agent.status.toLowerCase()}`} />
                  {agent.status}
                  {SHOW_STATUS_SINCE.has(agent.status) &&
                    ` · since ${formatDateTime(agent.statusSinceUtc)}`}
                </div>
                <div className="detail-header-meta-line">Agent</div>
              </div>
            </div>
            {developer && (
              <div className="detail-header-side">
                <div className="detail-header-actions">
                  {/* Deploy/Refresh/Apply moved to the Configuration tab, next
                      to the status cells they act on - the header keeps only
                      the always-relevant operational pair. */}
                  <button
                    type="button"
                    className="logs-button"
                    onClick={handleDownloadLogs}
                    disabled={downloadingLogs}
                  >
                    <span className="label-full">{downloadingLogs ? "Fetching…" : "Download logs"}</span>
                    <span className="label-short">{downloadingLogs ? "…" : "Logs"}</span>
                  </button>
                  <button
                    type="button"
                    className="restart-button"
                    onClick={() => setRestartConfirmOpen(true)}
                    disabled={restarting}
                  >
                    <span className="label-full">{restarting ? "Restarting…" : "Restart"}</span>
                    <span className="label-short">{restarting ? "…" : "Restart"}</span>
                  </button>
                </div>
              </div>
            )}
          </div>

          {restartMessage && <p className="restart-message">{restartMessage}</p>}
          {logsMessage && <p className="restart-message">{logsMessage}</p>}

          {agent.error && <ErrorBanner message={agent.error} />}

          <ConfirmDialog
            open={restartConfirmOpen}
            message={`Restart agent ${agentId}? Monitoring on this agent will be briefly offline while it restarts.`}
            confirmLabel="Restart"
            onConfirm={handleRestart}
            onCancel={() => setRestartConfirmOpen(false)}
          />

          <ConfirmDialog
            open={deployConfirmOpen}
            message={`Deploy the latest image to agent ${agentId}? This pulls the latest build and recreates the container - monitoring on this agent will be briefly offline.`}
            confirmLabel="Deploy"
            onConfirm={handleDeploy}
            onCancel={() => setDeployConfirmOpen(false)}
          />

          <ConfirmDialog
            open={refreshConfirmOpen}
            message={`Refresh configuration on agent ${agentId}? If the currently published configuration differs from what's applied, the agent will restart to adopt it.`}
            confirmLabel="Refresh"
            onConfirm={handleRefreshConfiguration}
            onCancel={() => setRefreshConfirmOpen(false)}
          />

          <div className="detail-tabs">
            <button
              type="button"
              className={`detail-tab${activeTab === "overview" ? " active" : ""}`}
              onClick={() => onSelectTab("overview")}
            >
              Overview
            </button>
            <button
              type="button"
              className={`detail-tab${activeTab === "activity" ? " active" : ""}`}
              onClick={() => onSelectTab("activity")}
            >
              Activity
            </button>
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
              {/* Heartbeat trio, same shape as DeviceDetail's Overview -
                  last heartbeat next to the expected interval reads as
                  "is it late?". */}
              <div className="metric-grid">
                <div className="metric-cell">
                  <div className="metric-cell-label">Last heartbeat</div>
                  <div className="metric-cell-value" title={formatDateTimeExact(agent.lastHeartbeatUtc)}>
                    {formatDateTime(agent.lastHeartbeatUtc)}
                  </div>
                </div>
                <div className="metric-cell">
                  <div className="metric-cell-label">Interval</div>
                  <div className="metric-cell-value">{formatInterval(agent.heartbeatInterval)}</div>
                </div>
                <div className="metric-cell">
                  <div className="metric-cell-label">Uptime</div>
                  <div className="metric-cell-value" title={formatDateTimeExact(agent.startedUtc)}>
                    {formatUptime(agent.startedUtc)}
                  </div>
                </div>
              </div>

              <h3 className="section-heading">Resource usage</h3>

              {!metrics ? <p>Loading metrics...</p> : <AgentMetricsChart samples={metrics} />}

              <h3 className="section-heading">Devices on this agent</h3>

              {!agentDevices ? (
                <p>Loading devices...</p>
              ) : agentDevices.length === 0 ? (
                <p>No devices reporting on this agent yet.</p>
              ) : (
                <div className="entity-list">
                  {agentDevices.map((device) => (
                    <DeviceRow
                      key={device.deviceId}
                      device={device}
                      onClick={() => onSelectDevice(device.deviceId)}
                    />
                  ))}
                </div>
              )}
            </>
          )}

          {activeTab === "activity" && (
            <CommandHistory agentId={agentId} />
          )}

          {activeTab === "configuration" && (
            <>
              {/* Decision-log.md ADR-077 - reuses the ConfigurationStatus/VersionStatus
                  fields already on AgentSummary (ADR-075), no separate
                  fetch. One card per concern, each card's actions beside
                  the status they act on (User Mode gets the cards
                  read-only). */}
              <div className="config-section">
                <div className="config-section-header">
                  <div>
                    <div className="metric-cell-label">Software</div>
                    <div className="metric-cell-value">
                      <span className={`status ${VERSION_STATUS_CLASS[agent.versionStatus.status] ?? "status-unknown"}`}>
                        {agent.versionStatus.status}
                      </span>
                      {agent.versionStatus.status !== "NeverDeployed" && (
                        <span>
                          {" "}Desired: {agent.versionStatus.desiredVersion ?? "—"} · Running:{" "}
                          {agent.versionStatus.runningVersion ?? "unknown"}
                        </span>
                      )}
                    </div>
                  </div>
                  {developer && (
                    <div className="detail-header-actions">
                      <button
                        type="button"
                        className="logs-button"
                        onClick={() => setDeployConfirmOpen(true)}
                        disabled={deploying}
                      >
                        {deploying ? "Deploying…" : "Deploy latest"}
                      </button>
                    </div>
                  )}
                </div>
                {deployMessage && <p className="restart-message">{deployMessage}</p>}
              </div>

              <div className="config-section">
                <div className="config-section-header">
                  <div>
                    <div className="metric-cell-label">Configuration</div>
                    <div className="metric-cell-value">
                      <span className={`status ${CONFIG_STATUS_CLASS[agent.configurationStatus.status] ?? "status-unknown"}`}>
                        {agent.configurationStatus.status}
                      </span>
                      {agent.configurationStatus.publishedVersion != null && (
                        <span>
                          {" "}Desired v{agent.configurationStatus.publishedVersion} · Applied{" "}
                          {agent.configurationStatus.appliedVersion != null
                            ? `v${agent.configurationStatus.appliedVersion}`
                            : "unknown"}
                        </span>
                      )}
                    </div>
                  </div>
                  {developer && (
                    <div className="detail-header-actions">
                      <button
                        type="button"
                        className="logs-button"
                        onClick={() => setRefreshConfirmOpen(true)}
                        disabled={refreshing}
                      >
                        {refreshing ? "Refreshing…" : "Refresh"}
                      </button>
                      <button
                        type="button"
                        className="logs-button"
                        onClick={() => setApplyInputOpen((prev) => !prev)}
                        disabled={applying}
                      >
                        {applying ? "Applying…" : "Apply version…"}
                      </button>
                    </div>
                  )}
                </div>
                {applyInputOpen && (
                  <div className="apply-config-row">
                    <input
                      type="number"
                      min={1}
                      className="form-input apply-config-input"
                      placeholder="Version"
                      value={applyVersionInput}
                      onChange={(e) => setApplyVersionInput(e.target.value)}
                      autoFocus
                    />
                    <button type="button" className="logs-button" onClick={handleApplyConfiguration}>
                      Apply
                    </button>
                    <button
                      type="button"
                      className="logs-button"
                      onClick={() => {
                        setApplyInputOpen(false);
                        setApplyVersionInput("");
                      }}
                    >
                      Cancel
                    </button>
                  </div>
                )}
                {refreshMessage && <p className="restart-message">{refreshMessage}</p>}
                {applyMessage && <p className="restart-message">{applyMessage}</p>}
              </div>

              {/* Publish/Rollback live inside this panel - Developer only,
                  like every other action on the page. */}
              {developer && registryEntry && (
                <>
                  <h3 className="section-heading">Published configuration</h3>
                  <div className="config-section">
                    <AgentConfigurationPanel registryAgentId={registryEntry.agentId} />
                  </div>
                </>
              )}

              <h3 className="section-heading">Agent info</h3>
              <div className="metric-grid">
                <div className="metric-cell">
                  <div className="metric-cell-label">Hostname</div>
                  <div className="metric-cell-value">{agent.hostName}</div>
                </div>
                <div className="metric-cell">
                  <div className="metric-cell-label">Firmware</div>
                  <div className="metric-cell-value">{agent.firmwareVersion || "—"}</div>
                </div>
                <div className="metric-cell">
                  <div className="metric-cell-label">Runtime</div>
                  <div className="metric-cell-value">{agent.runtimeVersion || "—"}</div>
                </div>
                <div className="metric-cell">
                  <div className="metric-cell-label">OS</div>
                  <div className="metric-cell-value" title={agent.osDescription}>
                    {agent.osDescription || "—"}
                  </div>
                </div>
              </div>
            </>
          )}
        </>
      )}
    </div>
  );
}
