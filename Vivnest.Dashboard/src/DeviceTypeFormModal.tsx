import { useEffect, useState } from "react";
import type { DeviceTypeAdmin, DeviceTypeStatus } from "./api";

interface DeviceTypeFormModalProps {
  open: boolean;
  initial: DeviceTypeAdmin | null;
  // A save failure - shown inline instead of closing the modal, so a
  // validation error doesn't lose everything the user just filled in on
  // this form.
  error?: string | null;
  onSave: (name: string, description: string, status: DeviceTypeStatus) => void;
  onCancel: () => void;
}

const STATUS_OPTIONS: DeviceTypeStatus[] = ["Active", "Inactive"];

// Mirrors MachineFormModal.tsx's shape (decision-log.md ADR-057) - Status
// shown only when editing, Create always starts Active server-side.
export function DeviceTypeFormModal({ open, initial, error, onSave, onCancel }: DeviceTypeFormModalProps) {
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [status, setStatus] = useState<DeviceTypeStatus>("Active");

  useEffect(() => {
    if (!open) return;

    setName(initial?.deviceTypeName ?? "");
    setDescription(initial?.description ?? "");
    setStatus(initial?.status ?? "Active");
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
        <div className="form-field">
          <label className="form-label" htmlFor="device-type-description">
            Description
          </label>
          <input
            id="device-type-description"
            className="form-input"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Optional"
          />
        </div>
        {isEdit && (
          <div className="form-field">
            <label className="form-label" htmlFor="device-type-status">
              Status
            </label>
            <select
              id="device-type-status"
              className="form-select"
              value={status}
              onChange={(e) => setStatus(e.target.value as DeviceTypeStatus)}
            >
              {STATUS_OPTIONS.map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </select>
          </div>
        )}
        {error && <p className="form-dialog-error">{error}</p>}
        <div className="confirm-dialog-actions">
          <button type="button" className="confirm-dialog-cancel" onClick={onCancel}>
            Cancel
          </button>
          <button
            type="button"
            className="form-dialog-save"
            disabled={!canSave}
            onClick={() => onSave(name.trim(), description.trim(), status)}
          >
            Save
          </button>
        </div>
      </div>
    </div>
  );
}
