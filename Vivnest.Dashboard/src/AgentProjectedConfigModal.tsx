import { useEffect, useState } from "react";
import {
  ApiError,
  getProjectedAgentConfig,
  publishAgentConfig,
  type AgentPublishResult,
  type AgentRegistry,
  type ProjectedAgentConfig,
} from "./api";

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

  useEffect(() => {
    if (!open || !agent) return;

    setProjected(null);
    setError(null);
    setPublishResult(null);

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
