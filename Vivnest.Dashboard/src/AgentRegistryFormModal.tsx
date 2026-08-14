import { useEffect, useState } from "react";
import type { AgentRegistry, AgentRegistryStatus, AgentRegistryType } from "./api";

interface AgentRegistryFormModalProps {
  open: boolean;
  initial: AgentRegistry | null;
  onSave: (
    name: string,
    description: string,
    status: AgentRegistryStatus,
    firmwareVersion: string,
    type: AgentRegistryType,
    runtimeAgentId: string,
  ) => void;
  onCancel: () => void;
}

const TYPE_OPTIONS: { value: AgentRegistryType; label: string }[] = [
  { value: "Low", label: "Low" },
  { value: "High", label: "High" },
];

const STATUS_OPTIONS: { value: AgentRegistryStatus; label: string }[] = [
  { value: "Active", label: "Active" },
  { value: "Inactive", label: "Inactive" },
];

// Mirrors CapabilityFormModal.tsx exactly - same .confirm-overlay/
// .form-dialog reuse, just four fields instead of two. The Capabilities
// checklist that used to sit here was removed (decision-log.md ADR-059) -
// which capabilities an Agent declares is AgentCapability's job now
// (Assign/Unassign lifecycle, no dashboard UI yet, same "backend first"
// sequencing DeviceCapability followed in ADR-057).
export function AgentRegistryFormModal({ open, initial, onSave, onCancel }: AgentRegistryFormModalProps) {
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [status, setStatus] = useState<AgentRegistryStatus>("Active");
  const [firmwareVersion, setFirmwareVersion] = useState("");
  const [type, setType] = useState<AgentRegistryType>("Low");
  const [runtimeAgentId, setRuntimeAgentId] = useState("");

  useEffect(() => {
    if (!open) return;

    setName(initial?.name ?? "");
    setDescription(initial?.description ?? "");
    setStatus(initial?.status ?? "Active");
    setFirmwareVersion(initial?.firmwareVersion ?? "");
    setType(initial?.type ?? "Low");
    setRuntimeAgentId(initial?.runtimeAgentId ?? "");
  }, [open, initial]);

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

  if (!open) return null;

  const isEdit = initial !== null;
  const canSave = name.trim().length > 0;

  return (
    <div className="confirm-overlay" onClick={onCancel}>
      <div
        className="form-dialog"
        role="dialog"
        aria-modal="true"
        aria-label={isEdit ? "Edit agent" : "Add agent"}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="form-field">
          <label className="form-label" htmlFor="agent-name">
            Name
          </label>
          <input
            id="agent-name"
            className="form-input"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="1Fitz Capture Agent"
            autoFocus
          />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="agent-description">
            Description
          </label>
          <input
            id="agent-description"
            className="form-input"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Optional"
          />
        </div>
        {isEdit && (
          <div className="form-field">
            <label className="form-label" htmlFor="agent-status">
              Status
            </label>
            <select
              id="agent-status"
              className="form-select"
              value={status}
              onChange={(e) => setStatus(e.target.value as AgentRegistryStatus)}
            >
              {STATUS_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </div>
        )}
        <div className="form-field">
          <label className="form-label" htmlFor="agent-firmware">
            Firmware version
          </label>
          <input
            id="agent-firmware"
            className="form-input"
            value={firmwareVersion}
            onChange={(e) => setFirmwareVersion(e.target.value)}
            placeholder="1.0.0"
          />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="agent-type">
            Type
          </label>
          <select
            id="agent-type"
            className="form-select"
            value={type}
            onChange={(e) => setType(e.target.value as AgentRegistryType)}
          >
            {TYPE_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="agent-runtime-id">
            Runtime Agent Id
          </label>
          <input
            id="agent-runtime-id"
            className="form-input"
            value={runtimeAgentId}
            onChange={(e) => setRuntimeAgentId(e.target.value)}
            placeholder="Optional - the real Agent:AgentId from this agent's appsettings.json"
          />
          <p className="form-hint">
            Links this admin Agent to the real Vivnest.Agent process it corresponds to.
          </p>
        </div>
        <div className="confirm-dialog-actions">
          <button type="button" className="confirm-dialog-cancel" onClick={onCancel}>
            Cancel
          </button>
          <button
            type="button"
            className="form-dialog-save"
            disabled={!canSave}
            onClick={() =>
              onSave(name.trim(), description.trim(), status, firmwareVersion.trim(), type, runtimeAgentId.trim())
            }
          >
            Save
          </button>
        </div>
      </div>
    </div>
  );
}
