import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import {
  getProjectedDeviceConfig,
  publishDeviceConfig,
  rollbackDeviceConfig,
  type ConfigurationSyncStatus,
  type DevicePublishResult,
} from "./api";
import { ErrorState } from "./ErrorState";
import { useApiKey } from "./session";

// Decision-log.md ADR-068 - reuses the existing .status/.status-* badge
// vocabulary (App.css) rather than introducing new styles.
const SYNC_STATUS_CLASS: Record<ConfigurationSyncStatus, string> = {
  UpToDate: "status-online",
  Pending: "status-warning",
  Failed: "status-error",
  NeverPublished: "status-unknown",
  Unknown: "status-unknown",
};

interface DeviceConfigurationPanelProps {
  // The ADMIN registry id - projected config is keyed by it, not by the
  // runtime device id. Hosts that only have a runtime id resolve the
  // link (DeviceRegistry.runtimeDeviceId) before rendering this.
  registryDeviceId: string;
}

// The Desired -> Published -> Applied story for one Device, promoted out
// of its old modal into a first-class panel (dashboard-redesign-plan.md
// D3): sync status, publish, rollback, warnings, and the projected
// device-config/*.json document (decision-log.md ADR-063/064/068/070).
// Publish stays disabled while any warning is present, since the
// backend's own hard gate would refuse it anyway.
export function DeviceConfigurationPanel({ registryDeviceId }: DeviceConfigurationPanelProps) {
  const apiKey = useApiKey();
  const queryClient = useQueryClient();

  const projectedQuery = useQuery({
    queryKey: ["projected-device-config", registryDeviceId],
    queryFn: () => getProjectedDeviceConfig(apiKey, registryDeviceId),
  });

  const [error, setError] = useState<string | null>(null);
  const [publishResult, setPublishResult] = useState<DevicePublishResult | null>(null);
  const [rollbackVersion, setRollbackVersion] = useState("");

  // Publishing changes the sync status the device list and detail render.
  const invalidateStatus = () => {
    queryClient.invalidateQueries({ queryKey: ["projected-device-config", registryDeviceId] });
    queryClient.invalidateQueries({ queryKey: ["devices"] });
    queryClient.invalidateQueries({ queryKey: ["device-registry"] });
  };

  const publishMutation = useMutation({
    mutationFn: () => publishDeviceConfig(apiKey, registryDeviceId),
    onSuccess: (result) => {
      setPublishResult(result);
      invalidateStatus();
    },
    onError: (err) => setError(err.message),
  });

  const rollbackMutation = useMutation({
    mutationFn: (targetVersion: number) => rollbackDeviceConfig(apiKey, registryDeviceId, targetVersion),
    onSuccess: (result) => {
      setPublishResult(result);
      invalidateStatus();
    },
    onError: (err) => setError(err.message),
  });

  const displayed = publishResult?.document ?? projectedQuery.data ?? null;

  if (projectedQuery.isError) {
    return <ErrorState message={projectedQuery.error.message} onRetry={() => projectedQuery.refetch()} />;
  }
  if (!displayed) return <p>Loading projected config...</p>;

  function handleRollback() {
    const targetVersion = Number(rollbackVersion);

    if (!Number.isInteger(targetVersion) || targetVersion < 1) return;

    setError(null);
    rollbackMutation.mutate(targetVersion);
  }

  return (
    <>
      <p className="form-hint">
        Preview of what would be written to the real device-config file. Publish only becomes
        available once every warning below is resolved.
      </p>

      {error && <p className="form-dialog-error">{error}</p>}

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

      <div className="confirm-dialog-actions" style={{ justifyContent: "flex-start" }}>
        <button
          type="button"
          className="confirm-dialog-confirm"
          disabled={displayed.warnings.length > 0 || publishMutation.isPending}
          onClick={() => {
            setError(null);
            publishMutation.mutate();
          }}
        >
          {publishMutation.isPending ? "Publishing..." : "Publish"}
        </button>
        <input
          type="number"
          min={1}
          step={1}
          placeholder="Version #"
          value={rollbackVersion}
          onChange={(event) => setRollbackVersion(event.target.value)}
          disabled={displayed.warnings.length > 0 || rollbackMutation.isPending}
          style={{ width: "8rem" }}
        />
        <button
          type="button"
          className="confirm-dialog-cancel"
          disabled={
            displayed.warnings.length > 0 ||
            rollbackMutation.isPending ||
            !Number.isInteger(Number(rollbackVersion)) ||
            Number(rollbackVersion) < 1
          }
          onClick={handleRollback}
        >
          {rollbackMutation.isPending ? "Rolling back..." : "Roll back"}
        </button>
      </div>
      <p className="form-hint">
        Roll back republishes an old version's content as a brand-new version - never mutates the
        old version.
      </p>

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
  );
}
