import { useEffect, useState } from "react";
import {
  ApiError,
  getProjectedAgentConfig,
  publishAgentConfig,
  rollbackAgentConfig,
  type AgentPublishResult,
  type AgentRegistry,
  type ConfigurationSyncStatus,
  type ProjectedAgentConfig,
} from "./api";

// Decision-log.md ADR-068 - see ProjectedConfigModal.tsx's own copy.
const SYNC_STATUS_CLASS: Record<ConfigurationSyncStatus, string> = {
  UpToDate: "status-online",
  Pending: "status-warning",
  Failed: "status-error",
  NeverPublished: "status-unknown",
  Unknown: "status-unknown",
};

interface AgentProjectedConfigModalProps {
  open: boolean;
  agent: AgentRegistry | null;
  apiKey: string;
  onAuthError: () => void;
  onClose: () => void;
}

// Preview + publish of what the Admin domain projects as this Agent's
// real agent-config/{agentId}.json "AiClassification" section
// (decision-log.md ADR-064) - mirrors ProjectedConfigModal.tsx on the
// Device side, built from every DeviceCapability across the tenant/site
// whose ExecutingAgentId is this Agent. Publishing only ever replaces the
// "AiClassification" key on the real blob - every other section (e.g. a
// Low-type agent's "HomeAssistant") is left untouched.
export function AgentProjectedConfigModal({ open, agent, apiKey, onAuthError, onClose }: AgentProjectedConfigModalProps) {
  const [projected, setProjected] = useState<ProjectedAgentConfig | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [publishing, setPublishing] = useState(false);
  const [publishResult, setPublishResult] = useState<AgentPublishResult | null>(null);
  const [rollbackVersion, setRollbackVersion] = useState("");
  const [rollingBack, setRollingBack] = useState(false);

  useEffect(() => {
    if (!open || !agent) return;

    setProjected(null);
    setError(null);
    setPublishResult(null);
    setRollbackVersion("");

    getProjectedAgentConfig(apiKey, agent.agentId)
      .then(setProjected)
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) {
          onAuthError();
          return;
        }

        setError(err instanceof Error ? err.message : "Something went wrong.");
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, agent]);

  useEffect(() => {
    if (!open) return;

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        onClose();
      }
    }

    window.addEventListener("keydown", handleKeyDown);

    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [open, onClose]);

  if (!open || !agent) return null;

  const displayed = publishResult?.document ?? projected;

  function handlePublish() {
    if (!agent) return;

    setPublishing(true);
    setError(null);

    publishAgentConfig(apiKey, agent.agentId)
      .then(setPublishResult)
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) {
          onAuthError();
          return;
        }

        setError(err instanceof Error ? err.message : "Something went wrong.");
      })
      .finally(() => setPublishing(false));
  }

  function handleRollback() {
    if (!agent) return;

    const targetVersion = Number(rollbackVersion);

    if (!Number.isInteger(targetVersion) || targetVersion < 1) return;

    setRollingBack(true);
    setError(null);

    rollbackAgentConfig(apiKey, agent.agentId, targetVersion)
      .then(setPublishResult)
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) {
          onAuthError();
          return;
        }

        setError(err instanceof Error ? err.message : "Something went wrong.");
      })
      .finally(() => setRollingBack(false));
  }

  return (
    <div className="confirm-overlay" onClick={onClose}>
      <div
        className="form-dialog"
        role="dialog"
        aria-modal="true"
        aria-label={`${agent.name} - Projected Config`}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="form-field">
          <label className="form-label">{agent.name} - Projected Config</label>
          <p className="form-hint">
            Preview of the "AiClassification" section that would be written to the real agent-config file -
            every other section stays untouched. Publish only becomes available once every warning below is
            resolved.
          </p>
        </div>

        {error && <p className="form-dialog-error">{error}</p>}

        {!error && !displayed && <p>Loading projected config...</p>}

        {displayed && (
          <>
            {displayed.syncStatus && (
              <div className="form-field">
                <label className="form-label">Sync Status</label>
                <p>
                  <span className={`status ${SYNC_STATUS_CLASS[displayed.syncStatus.status]}`}>
                    {displayed.syncStatus.status}
                  </span>
                </p>
                <p className="form-hint">
                  Published: {displayed.syncStatus.publishedUtc ?? "never"}
                  {displayed.syncStatus.publishedVersion != null && ` (v${displayed.syncStatus.publishedVersion})`}
                  {" · "}
                  Applied: {displayed.syncStatus.appliedUtc ?? "unknown"}
                  {displayed.syncStatus.appliedVersion != null && ` (v${displayed.syncStatus.appliedVersion})`}
                </p>
                {displayed.syncStatus.applyError && (
                  <p className="form-dialog-error">{displayed.syncStatus.applyError}</p>
                )}
              </div>
            )}

            <div className="form-field">
              <label className="form-label">Rollback</label>
              <p className="form-hint">
                Republishes an old version's content as a brand-new version - never mutates the old version.
              </p>
              <div className="confirm-dialog-actions" style={{ justifyContent: "flex-start" }}>
                <input
                  type="number"
                  min={1}
                  step={1}
                  placeholder="Version #"
                  value={rollbackVersion}
                  onChange={(event) => setRollbackVersion(event.target.value)}
                  disabled={displayed.warnings.length > 0 || rollingBack}
                  style={{ width: "8rem" }}
                />
                <button
                  type="button"
                  className="confirm-dialog-cancel"
                  disabled={
                    displayed.warnings.length > 0 ||
                    rollingBack ||
                    !Number.isInteger(Number(rollbackVersion)) ||
                    Number(rollbackVersion) < 1
                  }
                  onClick={handleRollback}
                >
                  {rollingBack ? "Rolling back..." : "Roll back"}
                </button>
              </div>
            </div>

            {displayed.warnings.length > 0 && (
              <div className="form-field">
                <label className="form-label">Warnings</label>
                {displayed.warnings.map((warning, index) => (
                  <p className="form-dialog-error" key={index}>
                    {warning}
                  </p>
                ))}
              </div>
            )}

            {publishResult && (
              <p className={publishResult.published ? "form-hint" : "form-dialog-error"}>
                {publishResult.published
                  ? "Published to the real agent-config file's AiClassification section."
                  : publishResult.reason ?? "Publish was blocked."}
              </p>
            )}

            <div className="form-field">
              <pre className="form-json-preview">
                {JSON.stringify({ AgentId: displayed.agentId, Devices: displayed.devices }, null, 2)}
              </pre>
            </div>
          </>
        )}

        <div className="confirm-dialog-actions">
          <button type="button" className="confirm-dialog-cancel" onClick={onClose}>
            Close
          </button>
          <button
            type="button"
            className="confirm-dialog-confirm"
            disabled={!displayed || displayed.warnings.length > 0 || publishing}
            onClick={handlePublish}
          >
            {publishing ? "Publishing..." : "Publish"}
          </button>
        </div>
      </div>
    </div>
  );
}
