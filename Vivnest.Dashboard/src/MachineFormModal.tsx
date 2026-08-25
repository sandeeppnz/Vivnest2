import { useEffect, useState } from "react";
import type { MachineAdmin, MachineStatus } from "./api";

interface MachineFormModalProps {
  open: boolean;
  initial: MachineAdmin | null;
  // A save failure - shown inline instead of closing the modal, so a
  // validation error doesn't lose everything the user just filled in on
  // this form.
  error?: string | null;
  onSave: (
    name: string,
    hostname: string,
    description: string,
    operatingSystem: string,
    architecture: string,
    status: MachineStatus,
  ) => void;
  onCancel: () => void;
}

const STATUS_OPTIONS: MachineStatus[] = ["Active", "Offline", "Retired", "Decommissioned"];

// Mirrors DeviceTypeFormModal.tsx's shape - no delete, since MachinesFunction
// has no DELETE route (a Machine's identity should remain stable for its
// lifetime, see decision-log.md ADR-053; retire via Status instead). Status
// shown only when editing - Create always starts Active server-side.
export function MachineFormModal({ open, initial, error, onSave, onCancel }: MachineFormModalProps) {
  const [name, setName] = useState("");
  const [hostname, setHostname] = useState("");
  const [description, setDescription] = useState("");
  const [operatingSystem, setOperatingSystem] = useState("");
  const [architecture, setArchitecture] = useState("");
  const [status, setStatus] = useState<MachineStatus>("Active");

  useEffect(() => {
    if (!open) return;

    setName(initial?.name ?? "");
    setHostname(initial?.hostname ?? "");
    setDescription(initial?.description ?? "");
    setOperatingSystem(initial?.operatingSystem ?? "");
    setArchitecture(initial?.architecture ?? "");
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
        aria-label={isEdit ? "Edit machine" : "Add machine"}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="form-field">
          <label className="form-label" htmlFor="machine-name">
            Name
          </label>
          <input
            id="machine-name"
            className="form-input"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Raspberry Pi 4 (living room)"
            autoFocus
          />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="machine-hostname">
            Hostname
          </label>
          <input
            id="machine-hostname"
            className="form-input"
            value={hostname}
            onChange={(e) => setHostname(e.target.value)}
            placeholder="Optional"
          />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="machine-description">
            Description
          </label>
          <input
            id="machine-description"
            className="form-input"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Optional"
          />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="machine-os">
            Operating system
          </label>
          <input
            id="machine-os"
            className="form-input"
            value={operatingSystem}
            onChange={(e) => setOperatingSystem(e.target.value)}
            placeholder="Optional"
          />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="machine-arch">
            Architecture
          </label>
          <input
            id="machine-arch"
            className="form-input"
            value={architecture}
            onChange={(e) => setArchitecture(e.target.value)}
            placeholder="Optional"
          />
        </div>
        {isEdit && (
          <div className="form-field">
            <label className="form-label" htmlFor="machine-status">
              Status
            </label>
            <select
              id="machine-status"
              className="form-select"
              value={status}
              onChange={(e) => setStatus(e.target.value as MachineStatus)}
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
            onClick={() =>
              onSave(name.trim(), hostname.trim(), description.trim(), operatingSystem.trim(), architecture.trim(), status)
            }
          >
            Save
          </button>
        </div>
      </div>
    </div>
  );
}
