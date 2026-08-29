import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import {
  getProjectedAgentConfig,
  publishAgentConfig,
  rollbackAgentConfig,
  type AgentPublishResult,
  type ConfigurationSyncStatus,
} from "./api";
import { ErrorState } from "./ErrorState";
import { useApiKey } from "./session";

// Decision-log.md ADR-068 - see DeviceConfigurationPanel's own copy.
const SYNC_STATUS_CLASS: Record<ConfigurationSyncStatus, string> = {
  UpToDate: "status-online",
  Pending: "status-warning",
  Failed: "status-error",
  NeverPublished: "status-unknown",
  Unknown: "status-unknown",
};

interface AgentConfigurationPanelProps {
  // The ADMIN registry id (AgentRegistry.agentId), not the runtime agent
  // id - hosts with only a runtime id resolve the link
  // (AgentRegistry.runtimeAgentId) before rendering this.
  registryAgentId: string;
}

// The Agent-side twin of DeviceConfigurationPanel
// (dashboard-redesign-plan.md D3): the projected "AiClassification"
// section of agent-config/{agentId}.json (decision-log.md ADR-064) with
// sync status, publish and rollback. Publishing replaces the
// "AiClassification" key plus the Admin registry's own "Name" (ADR-087) -
// every other section (e.g. a Low-type agent's "HomeAssistant") is left
// untouched.
export function AgentConfigurationPanel({ registryAgentId }: AgentConfigurationPanelProps) {
  const apiKey = useApiKey();
  const queryClient = useQueryClient();

  const projectedQuery = useQuery({
    queryKey: ["projected-agent-config", registryAgentId],
    queryFn: () => getProjectedAgentConfig(apiKey, registryAgentId),
  });

  const [error, setError] = useState<string | null>(null);
  const [publishResult, setPublishResult] = useState<AgentPublishResult | null>(null);
  const [rollbackVersion, setRollbackVersion] = useState("");

  const invalidateStatus = () => {
    queryClient.invalidateQueries({ queryKey: ["projected-agent-config", registryAgentId] });
    queryClient.invalidateQueries({ queryKey: ["agents"] });
    queryClient.invalidateQueries({ queryKey: ["agent-registry"] });
  };

  const publishMutation = useMutation({
    mutationFn: () => publishAgentConfig(apiKey, registryAgentId),
    onSuccess: (result) => {
      setPublishResult(result);
      invalidateStatus();
    },
    onError: (err) => setError(err.message),
  });

  const rollbackMutation = useMutation({
    mutationFn: (targetVersion: number) => rollbackAgentConfig(apiKey, registryAgentId, targetVersion),
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

  // Reads top to bottom as status -> actions -> preview: sync state
  // first, publish/rollback right under it, and the projected JSON last
  // with its explanatory hint beside it rather than at the top of the
  // panel describing something two screens down.
  return (
    <>
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
            ? "Published to the real agent-config file's AiClassification section and Name."
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
        <label className="form-label">Projected content</label>
        <p className="form-hint">
          Preview of the "AiClassification" section that would be written to the real agent-config
          file - every other section stays untouched. Publish only becomes available once every
          warning above is resolved.
        </p>
        <pre className="form-json-preview">
          {JSON.stringify(
            { AgentId: displayed.agentId, Name: displayed.name, Devices: displayed.devices },
            null,
            2,
          )}
        </pre>
      </div>
    </>
  );
}
