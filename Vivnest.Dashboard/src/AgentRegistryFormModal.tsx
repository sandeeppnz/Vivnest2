import { useEffect, useState } from "react";
import type { AgentRegistry, AgentRegistryType } from "./api";

interface AgentRegistryFormModalProps {
  open: boolean;
  initial: AgentRegistry | null;
  onSave: (name: string, firmwareVersion: string, type: AgentRegistryType) => void;
  onCancel: () => void;
}

const TYPE_OPTIONS: { value: AgentRegistryType; label: string }[] = [
  { value: "Low", label: "Low" },
  { value: "High", label: "High" },
];

// Mirrors CapabilityFormModal.tsx exactly - same .confirm-overlay/
// .form-dialog reuse, just three fields instead of two.
export function AgentRegistryFormModal({ open, initial, onSave, onCancel }: AgentRegistryFormModalProps) {
  const [name, setName] = useState("");
  const [firmwareVersion, setFirmwareVersion] = useState("");
  const [type, setType] = useState<AgentRegistryType>("Low");

  useEffect(() => {
    if (!open) return;

    setName(initial?.name ?? "");
    setFirmwareVersion(initial?.firmwareVersion ?? "");
    setType(initial?.type ?? "Low");
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
        <div className="confirm-dialog-actions">
          <button type="button" className="confirm-dialog-cancel" onClick={onCancel}>
            Cancel
          </button>
          <button
            type="button"
            className="form-dialog-save"
            disabled={!canSave}
            onClick={() => onSave(name.trim(), firmwareVersion.trim(), type)}
          >
            Save
          </button>
        </div>
      </div>
    </div>
  );
}
