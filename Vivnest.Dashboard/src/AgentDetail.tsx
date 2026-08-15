import { useEffect, useState } from "react";
import {
  ApiError,
  deployAgent,
  getAgent,
  getAgentLogs,
  getAgentMetrics,
  getDevices,
  restartAgent,
  type AgentMetricSample,
  type AgentSummary,
  type DeviceSummary,
} from "./api";
import { formatDateTime, formatDateTimeExact, formatInterval, formatUptime } from "./format";
import { AgentIcon } from "./icons";
import { AgentMetricsChart } from "./AgentMetricsChart";
import { ConfirmDialog } from "./ConfirmDialog";
import { CopyIdButton } from "./CopyIdButton";
import { DeviceRow } from "./DeviceRow";
import { ErrorBanner } from "./ErrorBanner";

interface AgentDetailProps {
  apiKey: string;
  agentId: string;
  onBack: () => void;
  onSelectDevice: (deviceId: string) => void;
  onAuthError: () => void;
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

export function AgentDetail({
  apiKey,
  agentId,
  onBack,
  onSelectDevice,
  onAuthError,
}: AgentDetailProps) {
  const [agent, setAgent] = useState<AgentSummary | null>(null);
  const [devices, setDevices] = useState<DeviceSummary[] | null>(null);
  const [metrics, setMetrics] = useState<AgentMetricSample[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [restarting, setRestarting] = useState(false);
  const [restartMessage, setRestartMessage] = useState<string | null>(null);
  const [downloadingLogs, setDownloadingLogs] = useState(false);
  const [logsMessage, setLogsMessage] = useState<string | null>(null);
  const [deploying, setDeploying] = useState(false);
  const [deployMessage, setDeployMessage] = useState<string | null>(null);
  const [restartConfirmOpen, setRestartConfirmOpen] = useState(false);
  const [deployConfirmOpen, setDeployConfirmOpen] = useState(false);

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
    setMetrics(null);
    setError(null);

    getAgent(apiKey, agentId)
      .then((result) => !cancelled && setAgent(result))
      .catch(handleError);

    getDevices(apiKey)
      .then((result) => !cancelled && setDevices(result))
      .catch(handleError);

    getAgentMetrics(apiKey, agentId)
      .then((result) => !cancelled && setMetrics(result))
      .catch(handleError);

    return () => {
      cancelled = true;
    };
  }, [apiKey, agentId, onAuthError]);

  const agentDevices = devices?.filter((d) => d.agentId === agentId) ?? null;

  async function handleRestart() {
    setRestartConfirmOpen(false);
    setRestarting(true);
    setRestartMessage(null);

    try {
      await restartAgent(apiKey, agentId);

      setRestartMessage("Restart requested. The agent should reconnect shortly.");
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        onAuthError();
        return;
      }

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
      if (err instanceof ApiError && err.status === 401) {
        onAuthError();
        return;
      }

      setDeployMessage(
        err instanceof Error ? err.message : "Failed to request deploy.",
      );
    } finally {
      setDeploying(false);
    }
  }

  async function handleDownloadLogs() {
    setDownloadingLogs(true);
    setLogsMessage(null);

    try {
      const { url } = await getAgentLogs(apiKey, agentId);

      window.open(url, "_blank");
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        onAuthError();
        return;
      }

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

      {error && <p className="error">{error}</p>}

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
                  {agent.status} · since {formatDateTime(agent.statusSinceUtc)}
                </div>
                <div className="detail-header-meta-line">Agent</div>
              </div>
            </div>
            <div className="detail-header-side">
              <div className="detail-header-actions">
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
                  className="logs-button"
                  onClick={() => setDeployConfirmOpen(true)}
                  disabled={deploying}
                >
                  <span className="label-full">{deploying ? "Deploying…" : "Deploy latest"}</span>
                  <span className="label-short">{deploying ? "…" : "Deploy"}</span>
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
          </div>

          {restartMessage && <p className="restart-message">{restartMessage}</p>}
          {logsMessage && <p className="restart-message">{logsMessage}</p>}
          {deployMessage && <p className="restart-message">{deployMessage}</p>}

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

          <div className="metric-grid">
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

          {/* Decision-log.md ADR-077 - reuses the ConfigurationStatus/VersionStatus
              fields already on AgentSummary (ADR-075), no separate fetch. */}
          <div className="metric-grid">
            <div className="metric-cell">
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
            <div className="metric-cell">
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
    </div>
  );
}
