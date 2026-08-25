import { useEffect, useState } from "react";
import type { AgentRegistry, MachineAdmin } from "./api";

interface InstallAgentModalProps {
  open: boolean;
  mode: "install" | "move";
  agent: AgentRegistry | null;
  machines: MachineAdmin[];
  // A save failure - shown inline instead of closing the modal, same
  // pattern as the registry form modals.
  error?: string | null;
  onSave: (machineId: string, containerId: string, imageName: string, imageVersion: string) => void;
  onCancel: () => void;
}

// Shared by both Install and Move (decision-log.md ADR-053/056) - same
// fields either way, just a different verb and backend call. Machine
// picker is by Name, resolved to MachineId on submit - same id-for-wire/
// name-for-display pattern as the API Keys screen's Tenant/Site picker.
export function InstallAgentModal({ open, mode, agent, machines, error, onSave, onCancel }: InstallAgentModalProps) {
  const [machineId, setMachineId] = useState("");
  const [containerId, setContainerId] = useState("");
  const [imageName, setImageName] = useState("");
  const [imageVersion, setImageVersion] = useState("");

  useEffect(() => {
    if (!open) return;

    setMachineId("");
    setContainerId("");
    setImageName("");
    setImageVersion("");
  }, [open, agent]);

  useEffect(() => {
    if (!open) return;

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        onCancel();
      }
    }

    window.addEventListener("keydown", handleKeyDown);

    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [open, onCancel]);

  if (!open || !agent) return null;

  const canSave = machineId.trim().length > 0;
  const title = mode === "install" ? `Install ${agent.name}` : `Move ${agent.name}`;

  return (
    <div className="confirm-overlay" onClick={onCancel}>
      <div
        className="form-dialog"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="form-field">
          <label className="form-label" htmlFor="install-machine">
            Machine
          </label>
          {machines.length === 0 ? (
            <p className="form-hint">No machines registered yet - add one under Admin &rarr; Machines first.</p>
          ) : (
            <select
              id="install-machine"
              className="form-select"
              value={machineId}
              onChange={(e) => setMachineId(e.target.value)}
              autoFocus
            >
              <option value="">Select a machine...</option>
              {machines.map((m) => (
                <option key={m.machineId} value={m.machineId}>
                  {m.name}
                </option>
              ))}
            </select>
          )}
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="install-container">
            Container ID
          </label>
          <input
            id="install-container"
            className="form-input"
            value={containerId}
            onChange={(e) => setContainerId(e.target.value)}
            placeholder="Optional"
          />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="install-image">
            Image name
          </label>
          <input
            id="install-image"
            className="form-input"
            value={imageName}
            onChange={(e) => setImageName(e.target.value)}
            placeholder="Optional"
          />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="install-image-version">
            Image version
          </label>
          <input
            id="install-image-version"
            className="form-input"
            value={imageVersion}
            onChange={(e) => setImageVersion(e.target.value)}
            placeholder="Optional"
          />
        </div>
        {error && <p className="form-dialog-error">{error}</p>}
        <div className="confirm-dialog-actions">
          <button type="button" className="confirm-dialog-cancel" onClick={onCancel}>
            Cancel
          </button>
          <button
            type="button"
            className="form-dialog-save"
            disabled={!canSave}
            onClick={() =>
              onSave(machineId, containerId.trim(), imageName.trim(), imageVersion.trim())
            }
          >
            {mode === "install" ? "Install" : "Move"}
          </button>
        </div>
      </div>
    </div>
  );
}
