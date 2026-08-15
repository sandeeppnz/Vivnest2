import { useEffect, useState } from "react";
import {
  ApiError,
  getProjectedDeviceConfig,
  publishDeviceConfig,
  rollbackDeviceConfig,
  type ConfigurationSyncStatus,
  type DevicePublishResult,
  type DeviceRegistry,
  type ProjectedDeviceConfig,
} from "./api";

// Decision-log.md ADR-068 - reuses the existing .status/.status-* badge
// vocabulary (App.css) rather than introducing new styles.
const SYNC_STATUS_CLASS: Record<ConfigurationSyncStatus, string> = {
  UpToDate: "status-online",
  Pending: "status-warning",
  Failed: "status-error",
  NeverPublished: "status-unknown",
  Unknown: "status-unknown",
};

interface ProjectedConfigModalProps {
  open: boolean;
  device: DeviceRegistry | null;
  apiKey: string;
  onAuthError: () => void;
  onClose: () => void;
}

// Preview + publish of what the Admin domain projects as this Device's
// runtime device-config/*.json shape (decision-log.md ADR-063, extended
// ADR-064). Warnings surface each unresolved link (RuntimeDeviceId not
// set, OwningAgentId's RuntimeAgentId not set, unmatched DeviceType, a
// capability with no registered runtime projector) - Publish stays
// disabled while any are present, since the backend's own hard gate would
// refuse it anyway.
export function ProjectedConfigModal({ open, device, apiKey, onAuthError, onClose }: ProjectedConfigModalProps) {
  const [projected, setProjected] = useState<ProjectedDeviceConfig | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [publishing, setPublishing] = useState(false);
  const [publishResult, setPublishResult] = useState<DevicePublishResult | null>(null);
  const [rollbackVersion, setRollbackVersion] = useState("");
  const [rollingBack, setRollingBack] = useState(false);

  useEffect(() => {
    if (!open || !device) return;

    setProjected(null);
    setError(null);
    setPublishResult(null);
    setRollbackVersion("");

    getProjectedDeviceConfig(apiKey, device.deviceId)
      .then(setProjected)
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) {
          onAuthError();
          return;
        }

        setError(err instanceof Error ? err.message : "Something went wrong.");
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, device]);

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

  if (!open || !device) return null;

  const displayed = publishResult?.document ?? projected;

  function handlePublish() {
    if (!device) return;

    setPublishing(true);
    setError(null);

    publishDeviceConfig(apiKey, device.deviceId)
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
    if (!device) return;

    const targetVersion = Number(rollbackVersion);

    if (!Number.isInteger(targetVersion) || targetVersion < 1) return;

    setRollingBack(true);
    setError(null);

    rollbackDeviceConfig(apiKey, device.deviceId, targetVersion)
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
        aria-label={`${device.name} - Projected Config`}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="form-field">
          <label className="form-label">{device.name} - Projected Config</label>
          <p className="form-hint">
            Preview of what would be written to the real device-config file. Publish only becomes available
            once every warning below is resolved.
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
                  ? "Published to the real device-config file."
                  : publishResult.reason ?? "Publish was blocked."}
              </p>
            )}

            <div className="form-field">
              <pre className="form-json-preview">
                {JSON.stringify(
                  {
                    DeviceId: displayed.deviceId,
                    Name: displayed.name,
                    Type: displayed.type,
                    Enabled: displayed.enabled,
                    Location: displayed.location,
                    Brand: displayed.brand,
                    Model: displayed.model,
                    Firmware: displayed.firmware,
                    OwningAgentId: displayed.owningAgentId,
                    Settings: displayed.settings,
                    Capabilities: displayed.capabilities,
                  },
                  null,
                  2,
                )}
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
