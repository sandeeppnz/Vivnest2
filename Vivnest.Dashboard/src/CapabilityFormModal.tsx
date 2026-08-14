import { useEffect, useState } from "react";
import type { CapabilityAdmin, CapabilityType } from "./api";

interface CapabilityFormModalProps {
  open: boolean;
  initial: CapabilityAdmin | null;
  onSave: (name: string, type: CapabilityType) => void;
  onCancel: () => void;
}

const TYPE_OPTIONS: { value: CapabilityType; label: string }[] = [
  { value: "Device", label: "Device" },
  { value: "Service", label: "Service" },
  { value: "System", label: "System" },
];

// Add/Edit form - unlike ConfirmDialog (message-only, no field slot), this
// needs real inputs, so it's its own component. Reuses .confirm-overlay for
// the backdrop and .confirm-dialog-actions/-cancel for the footer, but has
// its own .form-dialog container sized for labeled fields.
export function CapabilityFormModal({ open, initial, onSave, onCancel }: CapabilityFormModalProps) {
  const [name, setName] = useState("");
  const [type, setType] = useState<CapabilityType>("Device");

  useEffect(() => {
    if (!open) return;

    setName(initial?.capabilityName ?? "");
    setType(initial?.capabilityType ?? "Device");
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
        aria-label={isEdit ? "Edit capability" : "Add capability"}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="form-field">
          <label className="form-label" htmlFor="capability-name">
            Name
          </label>
          <input
            id="capability-name"
            className="form-input"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Image Capture"
            autoFocus
          />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="capability-type">
            Type
          </label>
          <select
            id="capability-type"
            className="form-select"
            value={type}
            onChange={(e) => setType(e.target.value as CapabilityType)}
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
            onClick={() => onSave(name.trim(), type)}
          >
            Save
          </button>
        </div>
      </div>
    </div>
  );
}
