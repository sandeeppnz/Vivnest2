import { useEffect, useState } from "react";
import type { DeviceTypeAdmin } from "./api";

interface DeviceTypeFormModalProps {
  open: boolean;
  initial: DeviceTypeAdmin | null;
  onSave: (name: string) => void;
  onCancel: () => void;
}

// Mirrors CapabilityFormModal.tsx, minus the Type dropdown - a Device Type
// has no "type of type" the way Capability does (decision-log.md ADR-047).
export function DeviceTypeFormModal({ open, initial, onSave, onCancel }: DeviceTypeFormModalProps) {
  const [name, setName] = useState("");

  useEffect(() => {
    if (!open) return;

    setName(initial?.deviceTypeName ?? "");
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
        aria-label={isEdit ? "Edit device type" : "Add device type"}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="form-field">
          <label className="form-label" htmlFor="device-type-name">
            Name
          </label>
          <input
            id="device-type-name"
            className="form-input"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Camera"
            autoFocus
          />
        </div>
        <div className="confirm-dialog-actions">
          <button type="button" className="confirm-dialog-cancel" onClick={onCancel}>
            Cancel
          </button>
          <button
            type="button"
            className="form-dialog-save"
            disabled={!canSave}
            onClick={() => onSave(name.trim())}
          >
            Save
          </button>
        </div>
      </div>
    </div>
  );
}
