import { useEffect, useState } from "react";
import type {
  CapabilityAdmin,
  CapabilityConfigurationField,
  CapabilityConfigurationFieldType,
  CapabilityStatus,
  CapabilityType,
} from "./api";
import { TrashIcon } from "./icons";

interface CapabilityFormModalProps {
  open: boolean;
  initial: CapabilityAdmin | null;
  onSave: (
    name: string,
    type: CapabilityType,
    status: CapabilityStatus,
    configurationSchema: CapabilityConfigurationField[],
    configurationSchemaVersion: number,
  ) => void;
  onCancel: () => void;
}

const TYPE_OPTIONS: { value: CapabilityType; label: string }[] = [
  { value: "Device", label: "Device" },
  { value: "Service", label: "Service" },
  { value: "System", label: "System" },
];

const STATUS_OPTIONS: CapabilityStatus[] = ["Active", "Retired"];

const FIELD_TYPE_OPTIONS: CapabilityConfigurationFieldType[] = ["String", "Number", "Boolean"];

// Editable form-state shape for one CapabilityConfigurationField - numbers/
// lists stay as raw text while the row is being edited (controlled inputs
// can't hold `null`), converted to the real typed shape only on Save.
interface SchemaFieldRow {
  name: string;
  type: CapabilityConfigurationFieldType;
  required: boolean;
  minimum: string;
  maximum: string;
  allowedValues: string;
  defaultValue: string;
}

function fieldToRow(field: CapabilityConfigurationField): SchemaFieldRow {
  return {
    name: field.name,
    type: field.type,
    required: field.required,
    minimum: field.minimum?.toString() ?? "",
    maximum: field.maximum?.toString() ?? "",
    allowedValues: (field.allowedValues ?? []).join(", "),
    defaultValue: field.defaultValue ?? "",
  };
}

// Rows with no Name are dropped - an in-progress "+ Add field" row the
// admin never filled in shouldn't be saved as a nameless field.
function rowsToSchema(rows: SchemaFieldRow[]): CapabilityConfigurationField[] {
  return rows
    .filter((row) => row.name.trim().length > 0)
    .map((row) => ({
      name: row.name.trim(),
      type: row.type,
      required: row.required,
      minimum: row.minimum.trim() ? Number(row.minimum) : null,
      maximum: row.maximum.trim() ? Number(row.maximum) : null,
      allowedValues: row.allowedValues.trim()
        ? row.allowedValues.split(",").map((v) => v.trim()).filter(Boolean)
        : null,
      defaultValue: row.defaultValue.trim() || null,
    }));
}

// Add/Edit form - unlike ConfirmDialog (message-only, no field slot), this
// needs real inputs, so it's its own component. Reuses .confirm-overlay for
// the backdrop and .confirm-dialog-actions/-cancel for the footer, but has
// its own .form-dialog container sized for labeled fields.
//
// Configuration Schema (decision-log.md ADR-062, Phase 5) is a repeatable
// row editor, same "+ Add.../remove per row" shape DeviceRegistryFormModal
// already established for its free-form Settings list - just richer rows
// (Name/Type/Required/Min/Max/AllowedValues/Default instead of a plain
// key-value pair). Status is shown only when editing, same pattern
// DeviceRegistryFormModal/MachineFormModal already use (Create always
// starts Active server-side). Deliberately no separate top-level "Default
// Configuration" editor - each field's own Default Value is the only place
// to set a default, avoiding two UI spots that mean almost the same thing
// (Capability.DefaultConfiguration still exists server-side for direct API
// use, this form just never populates it independently of field defaults).
export function CapabilityFormModal({ open, initial, onSave, onCancel }: CapabilityFormModalProps) {
  const [name, setName] = useState("");
  const [type, setType] = useState<CapabilityType>("Device");
  const [status, setStatus] = useState<CapabilityStatus>("Active");
  const [schemaVersion, setSchemaVersion] = useState(1);
  const [schemaRows, setSchemaRows] = useState<SchemaFieldRow[]>([]);

  useEffect(() => {
    if (!open) return;

    setName(initial?.capabilityName ?? "");
    setType(initial?.capabilityType ?? "Device");
    setStatus(initial?.status ?? "Active");
    setSchemaVersion(initial?.configurationSchemaVersion ?? 1);
    setSchemaRows(initial ? initial.configurationSchema.map(fieldToRow) : []);
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

  function updateRow(index: number, patch: Partial<SchemaFieldRow>) {
    setSchemaRows((current) => current.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  }

  function removeRow(index: number) {
    setSchemaRows((current) => current.filter((_, i) => i !== index));
  }

  function handleSave() {
    onSave(name.trim(), type, status, rowsToSchema(schemaRows), schemaVersion);
  }

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
        {isEdit && (
          <div className="form-field">
            <label className="form-label" htmlFor="capability-status">
              Status
            </label>
            <select
              id="capability-status"
              className="form-select"
              value={status}
              onChange={(e) => setStatus(e.target.value as CapabilityStatus)}
            >
              {STATUS_OPTIONS.map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </select>
          </div>
        )}
        <div className="form-field">
          <label className="form-label" htmlFor="capability-schema-version">
            Configuration Schema Version
          </label>
          <input
            id="capability-schema-version"
            type="number"
            min={1}
            className="form-input"
            value={schemaVersion}
            onChange={(e) => setSchemaVersion(Math.max(1, Number(e.target.value) || 1))}
          />
        </div>
        <div className="form-field">
          <label className="form-label">Configuration Schema</label>
          <p className="form-hint">
            What settings this capability needs when assigned to a device (e.g. Object Detection's
            confidenceThreshold: Number, 0-1, required).
          </p>
          {schemaRows.length > 0 && (
            <div className="form-schema-list">
              {schemaRows.map((row, index) => (
                <div className="form-schema-row" key={index}>
                  <div className="form-schema-row-header">
                    <input
                      type="text"
                      className="form-input"
                      value={row.name}
                      onChange={(e) => updateRow(index, { name: e.target.value })}
                      placeholder="confidenceThreshold"
                    />
                    <select
                      className="form-select"
                      value={row.type}
                      onChange={(e) => updateRow(index, { type: e.target.value as CapabilityConfigurationFieldType })}
                    >
                      {FIELD_TYPE_OPTIONS.map((option) => (
                        <option key={option} value={option}>
                          {option}
                        </option>
                      ))}
                    </select>
                    <label className="form-checklist-item">
                      <input
                        type="checkbox"
                        checked={row.required}
                        onChange={(e) => updateRow(index, { required: e.target.checked })}
                      />
                      Required
                    </label>
                    <button
                      type="button"
                      className="icon-button icon-button-danger"
                      aria-label={`Remove field ${row.name || index + 1}`}
                      onClick={() => removeRow(index)}
                    >
                      <TrashIcon />
                    </button>
                  </div>
                  {row.type === "Number" && (
                    <div className="form-schema-row-extra">
                      <input
                        type="number"
                        className="form-input"
                        value={row.minimum}
                        onChange={(e) => updateRow(index, { minimum: e.target.value })}
                        placeholder="Minimum"
                      />
                      <input
                        type="number"
                        className="form-input"
                        value={row.maximum}
                        onChange={(e) => updateRow(index, { maximum: e.target.value })}
                        placeholder="Maximum"
                      />
                    </div>
                  )}
                  {row.type === "String" && (
                    <input
                      type="text"
                      className="form-input"
                      value={row.allowedValues}
                      onChange={(e) => updateRow(index, { allowedValues: e.target.value })}
                      placeholder="Allowed values, comma-separated (optional) - e.g. low, medium, high"
                    />
                  )}
                  <input
                    type="text"
                    className="form-input"
                    value={row.defaultValue}
                    onChange={(e) => updateRow(index, { defaultValue: e.target.value })}
                    placeholder="Default value (optional)"
                  />
                </div>
              ))}
            </div>
          )}
          <button
            type="button"
            className="form-kv-add"
            onClick={() =>
              setSchemaRows((current) => [
                ...current,
                { name: "", type: "String", required: false, minimum: "", maximum: "", allowedValues: "", defaultValue: "" },
              ])
            }
          >
            + Add field
          </button>
        </div>
        <div className="confirm-dialog-actions">
          <button type="button" className="confirm-dialog-cancel" onClick={onCancel}>
            Cancel
          </button>
          <button
            type="button"
            className="form-dialog-save"
            disabled={!canSave}
            onClick={handleSave}
          >
            Save
          </button>
        </div>
      </div>
    </div>
  );
}
