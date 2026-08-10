import { useEffect, useState } from "react";
import type { AgentRegistry, AgentRegistryType, CapabilityAdmin } from "./api";

interface AgentRegistryFormModalProps {
  open: boolean;
  initial: AgentRegistry | null;
  capabilities: CapabilityAdmin[];
  onSave: (name: string, firmwareVersion: string, type: AgentRegistryType, capabilityIds: string[]) => void;
  onCancel: () => void;
}

const TYPE_OPTIONS: { value: AgentRegistryType; label: string }[] = [
  { value: "Low", label: "Low" },
  { value: "High", label: "High" },
];

// Mirrors CapabilityFormModal.tsx exactly - same .confirm-overlay/
// .form-dialog reuse, just four fields instead of two. Capabilities here
// are declared/planned (decision-log.md ADR-046), not derived from live
// device assignment the way Program.cs's own capability set is - so every
// agent type gets the same checklist, not just High-type.
export function AgentRegistryFormModal({ open, initial, capabilities, onSave, onCancel }: AgentRegistryFormModalProps) {
  const [name, setName] = useState("");
  const [firmwareVersion, setFirmwareVersion] = useState("");
  const [type, setType] = useState<AgentRegistryType>("Low");
  const [capabilityIds, setCapabilityIds] = useState<string[]>([]);

  useEffect(() => {
    if (!open) return;

    setName(initial?.name ?? "");
    setFirmwareVersion(initial?.firmwareVersion ?? "");
    setType(initial?.type ?? "Low");
    setCapabilityIds(initial?.capabilityIds ?? []);
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

  function toggleCapability(capabilityId: string) {
    setCapabilityIds((current) =>
      current.includes(capabilityId)
        ? current.filter((id) => id !== capabilityId)
        : [...current, capabilityId],
    );
  }

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
        <div className="form-field">
          <label className="form-label">Capabilities</label>
          {capabilities.length === 0 ? (
            <p className="form-hint">No capabilities defined yet - add one under Admin &rarr; Capabilities first.</p>
          ) : (
            <div className="form-checklist">
              {capabilities.map((capability) => (
                <label className="form-checklist-item" key={capability.capabilityId}>
                  <input
                    type="checkbox"
                    checked={capabilityIds.includes(capability.capabilityId)}
                    onChange={() => toggleCapability(capability.capabilityId)}
                  />
                  {capability.capabilityName}
                </label>
              ))}
            </div>
          )}
        </div>
        <div className="confirm-dialog-actions">
          <button type="button" className="confirm-dialog-cancel" onClick={onCancel}>
            Cancel
          </button>
          <button
            type="button"
            className="form-dialog-save"
            disabled={!canSave}
            onClick={() => onSave(name.trim(), firmwareVersion.trim(), type, capabilityIds)}
          >
            Save
          </button>
        </div>
      </div>
    </div>
  );
}
