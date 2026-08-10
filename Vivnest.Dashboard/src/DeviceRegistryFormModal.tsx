import { useEffect, useState } from "react";
import type { AgentRegistry, CapabilityAdmin, DeviceRegistry, DeviceRegistryFields, DeviceTypeAdmin } from "./api";
import { TrashIcon } from "./icons";

interface DeviceRegistryFormModalProps {
  open: boolean;
  initial: DeviceRegistry | null;
  deviceTypes: DeviceTypeAdmin[];
  agents: AgentRegistry[];
  capabilities: CapabilityAdmin[];
  // A save failure (e.g. the Settings credential guard) - shown inline
  // instead of closing the modal, so a validation error doesn't lose
  // everything the user just filled in on this 10-field form.
  error?: string | null;
  onSave: (fields: DeviceRegistryFields) => void;
  onCancel: () => void;
}

interface SettingRow {
  key: string;
  value: string;
}

function settingsToRows(settings: Record<string, string>): SettingRow[] {
  return Object.entries(settings).map(([key, value]) => ({ key, value }));
}

function rowsToSettings(rows: SettingRow[]): Record<string, string> {
  const settings: Record<string, string> = {};
  for (const row of rows) {
    const key = row.key.trim();
    if (key) settings[key] = row.value;
  }
  return settings;
}

// Mirrors AgentRegistryFormModal.tsx's shape (declared/planned data, same
// Capabilities checklist, decision-log.md ADR-048), extended with Device
// Type/Owning Agent lookups and a free-form Settings key-value list.
// Settings is for non-secret connection facts only (Host, Username,
// RtspUsername, MACAddress, ChildDeviceId, ...) - never Password/
// RtspPassword, which stay in the device's local *.secrets.json file and
// are never accepted by this admin API (see the warning text below and
// DeviceRegistryAdminFunction's server-side check).
export function DeviceRegistryFormModal({
  open,
  initial,
  deviceTypes,
  agents,
  capabilities,
  error,
  onSave,
  onCancel,
}: DeviceRegistryFormModalProps) {
  const [name, setName] = useState("");
  const [deviceTypeId, setDeviceTypeId] = useState("");
  const [owningAgentId, setOwningAgentId] = useState("");
  const [location, setLocation] = useState("");
  const [brand, setBrand] = useState("");
  const [model, setModel] = useState("");
  const [firmware, setFirmware] = useState("");
  const [enabled, setEnabled] = useState(true);
  const [capabilityIds, setCapabilityIds] = useState<string[]>([]);
  const [settingRows, setSettingRows] = useState<SettingRow[]>([]);

  useEffect(() => {
    if (!open) return;

    setName(initial?.name ?? "");
    setDeviceTypeId(initial?.deviceTypeId ?? "");
    setOwningAgentId(initial?.owningAgentId ?? "");
    setLocation(initial?.location ?? "");
    setBrand(initial?.brand ?? "");
    setModel(initial?.model ?? "");
    setFirmware(initial?.firmware ?? "");
    setEnabled(initial?.enabled ?? true);
    setCapabilityIds(initial?.capabilityIds ?? []);
    setSettingRows(initial ? settingsToRows(initial.settings) : []);
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

  function updateSettingRow(index: number, field: "key" | "value", value: string) {
    setSettingRows((current) =>
      current.map((row, i) => (i === index ? { ...row, [field]: value } : row)),
    );
  }

  function removeSettingRow(index: number) {
    setSettingRows((current) => current.filter((_, i) => i !== index));
  }

  function handleSave() {
    onSave({
      name: name.trim(),
      deviceTypeId,
      owningAgentId,
      location: location.trim(),
      brand: brand.trim(),
      model: model.trim(),
      firmware: firmware.trim(),
      enabled,
      capabilityIds,
      settings: rowsToSettings(settingRows),
    });
  }

  return (
    <div className="confirm-overlay" onClick={onCancel}>
      <div
        className="form-dialog"
        role="dialog"
        aria-modal="true"
        aria-label={isEdit ? "Edit device" : "Add device"}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="form-field">
          <label className="form-label" htmlFor="device-name">
            Name
          </label>
          <input
            id="device-name"
            className="form-input"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Kitchen Camera"
            autoFocus
          />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="device-type">
            Device Type
          </label>
          <select
            id="device-type"
            className="form-select"
            value={deviceTypeId}
            onChange={(e) => setDeviceTypeId(e.target.value)}
          >
            <option value="">Not set</option>
            {deviceTypes.map((d) => (
              <option key={d.deviceTypeId} value={d.deviceTypeId}>
                {d.deviceTypeName}
              </option>
            ))}
          </select>
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="device-owning-agent">
            Owning Agent
          </label>
          <select
            id="device-owning-agent"
            className="form-select"
            value={owningAgentId}
            onChange={(e) => setOwningAgentId(e.target.value)}
          >
            <option value="">Not assigned</option>
            {agents.map((a) => (
              <option key={a.agentId} value={a.agentId}>
                {a.name}
              </option>
            ))}
          </select>
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="device-location">
            Location
          </label>
          <input
            id="device-location"
            className="form-input"
            value={location}
            onChange={(e) => setLocation(e.target.value)}
            placeholder="Kitchen"
          />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="device-brand">
            Brand
          </label>
          <input
            id="device-brand"
            className="form-input"
            value={brand}
            onChange={(e) => setBrand(e.target.value)}
            placeholder="TP-Link"
          />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="device-model">
            Model
          </label>
          <input
            id="device-model"
            className="form-input"
            value={model}
            onChange={(e) => setModel(e.target.value)}
            placeholder="Tapo C120"
          />
        </div>
        <div className="form-field">
          <label className="form-label" htmlFor="device-firmware">
            Firmware
          </label>
          <input
            id="device-firmware"
            className="form-input"
            value={firmware}
            onChange={(e) => setFirmware(e.target.value)}
            placeholder="1.2.3"
          />
        </div>
        <div className="form-field">
          <label className="form-checklist-item">
            <input
              type="checkbox"
              checked={enabled}
              onChange={(e) => setEnabled(e.target.checked)}
            />
            Enabled
          </label>
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
        <div className="form-field">
          <label className="form-label">Settings</label>
          <p className="form-hint">
            Non-secret connection facts only (Host, Username, ...). Never enter passwords or tokens here -
            those stay in the device's local secrets file.
          </p>
          {settingRows.length > 0 && (
            <div className="form-kv-list">
              {settingRows.map((row, index) => (
                <div className="form-kv-row" key={index}>
                  <input
                    type="text"
                    className="form-input"
                    value={row.key}
                    onChange={(e) => updateSettingRow(index, "key", e.target.value)}
                    placeholder="Host"
                  />
                  <input
                    type="text"
                    className="form-input"
                    value={row.value}
                    onChange={(e) => updateSettingRow(index, "value", e.target.value)}
                    placeholder="192.168.1.50"
                  />
                  <button
                    type="button"
                    className="icon-button icon-button-danger"
                    aria-label={`Remove setting ${row.key || index + 1}`}
                    onClick={() => removeSettingRow(index)}
                  >
                    <TrashIcon />
                  </button>
                </div>
              ))}
            </div>
          )}
          <button
            type="button"
            className="form-kv-add"
            onClick={() => setSettingRows((current) => [...current, { key: "", value: "" }])}
          >
            + Add setting
          </button>
        </div>
        {error && <p className="form-dialog-error">{error}</p>}
        <div className="confirm-dialog-actions">
          <button type="button" className="confirm-dialog-cancel" onClick={onCancel}>
            Cancel
          </button>
          <button type="button" className="form-dialog-save" disabled={!canSave} onClick={handleSave}>
            Save
          </button>
        </div>
      </div>
    </div>
  );
}
