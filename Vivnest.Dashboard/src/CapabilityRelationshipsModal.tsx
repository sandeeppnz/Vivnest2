import { useEffect, useMemo, useState } from "react";
import {
  ApiError,
  addCapabilityDependency,
  addDeviceTypeCapability,
  getCapabilityDependencies,
  getDeviceTypeCapabilities,
  removeCapabilityDependency,
  removeDeviceTypeCapability,
  type CapabilityAdmin,
  type CapabilityDependency,
  type DeviceTypeAdmin,
  type DeviceTypeCapability,
} from "./api";
import { TrashIcon } from "./icons";

interface CapabilityRelationshipsModalProps {
  open: boolean;
  capability: CapabilityAdmin | null;
  capabilities: CapabilityAdmin[];
  deviceTypes: DeviceTypeAdmin[];
  apiKey: string;
  onAuthError: () => void;
  onClose: () => void;
}

// Answers "what does this Capability require, and which DeviceTypes can
// use it?" (decision-log.md ADR-062, Phase 5) - two simple lists in one
// modal, each shaped like AgentCapabilitiesModal (plain list +
// Add/Remove, existence is the fact, no ExecutingAgent/Enabled richness
// DeviceCapabilitiesModal needs). Both underlying tables are small and
// global, so both are fetched whole on open and filtered client-side to
// this one Capability - same pattern already used for
// capabilities/agents elsewhere in this dashboard. Only direct
// dependencies are shown (spec's own explicit minimum bar) - no
// transitive-chain rendering.
export function CapabilityRelationshipsModal({
  open,
  capability,
  capabilities,
  deviceTypes,
  apiKey,
  onAuthError,
  onClose,
}: CapabilityRelationshipsModalProps) {
  const [dependencies, setDependencies] = useState<CapabilityDependency[] | null>(null);
  const [compatibility, setCompatibility] = useState<DeviceTypeCapability[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [addingDependency, setAddingDependency] = useState(false);
  const [selectedDependsOn, setSelectedDependsOn] = useState("");
  const [addingCompatibility, setAddingCompatibility] = useState(false);
  const [selectedDeviceType, setSelectedDeviceType] = useState("");
  const [saving, setSaving] = useState(false);

  function handleError(err: unknown) {
    if (err instanceof ApiError && err.status === 401) {
      onAuthError();
      return;
    }

    setError(err instanceof Error ? err.message : "Something went wrong.");
  }

  function load() {
    setError(null);

    getCapabilityDependencies(apiKey).then(setDependencies).catch(handleError);
    getDeviceTypeCapabilities(apiKey).then(setCompatibility).catch(handleError);
  }

  useEffect(() => {
    if (!open || !capability) return;

    setDependencies(null);
    setCompatibility(null);
    setAddingDependency(false);
    setSelectedDependsOn("");
    setAddingCompatibility(false);
    setSelectedDeviceType("");
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, capability]);

  useEffect(() => {
    if (!open) return;

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        onClose();
      }
    }

    window.addEventListener("keydown", handleKeyDown);

    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [open, onClose]);

  const capabilityNameById = useMemo(() => {
    const map = new Map<string, string>();
    for (const c of capabilities) map.set(c.capabilityId, c.capabilityName);
    return map;
  }, [capabilities]);

  const deviceTypeNameById = useMemo(() => {
    const map = new Map<string, string>();
    for (const d of deviceTypes) map.set(d.deviceTypeId, d.deviceTypeName);
    return map;
  }, [deviceTypes]);

  const ownDependencies = useMemo(
    () => (dependencies ?? []).filter((d) => d.capabilityId === capability?.capabilityId),
    [dependencies, capability],
  );

  const ownCompatibility = useMemo(
    () => (compatibility ?? []).filter((c) => c.capabilityId === capability?.capabilityId),
    [compatibility, capability],
  );

  const dependableCapabilities = useMemo(() => {
    const already = new Set(ownDependencies.map((d) => d.dependsOnCapabilityId));
    return capabilities.filter((c) => c.capabilityId !== capability?.capabilityId && !already.has(c.capabilityId));
  }, [capabilities, ownDependencies, capability]);

  const availableDeviceTypes = useMemo(() => {
    const already = new Set(ownCompatibility.map((c) => c.deviceTypeId));
    return deviceTypes.filter((d) => !already.has(d.deviceTypeId));
  }, [deviceTypes, ownCompatibility]);

  if (!open || !capability) return null;

  async function handleAddDependency() {
    if (!capability || !selectedDependsOn) return;

    setSaving(true);
    setError(null);

    try {
      await addCapabilityDependency(apiKey, capability.capabilityId, selectedDependsOn);
      setAddingDependency(false);
      setSelectedDependsOn("");
      load();
    } catch (err) {
      handleError(err);
    } finally {
      setSaving(false);
    }
  }

  async function handleRemoveDependency(dependencyId: string) {
    setError(null);

    try {
      await removeCapabilityDependency(apiKey, dependencyId);
      load();
    } catch (err) {
      handleError(err);
    }
  }

  async function handleAddCompatibility() {
    if (!capability || !selectedDeviceType) return;

    setSaving(true);
    setError(null);

    try {
      await addDeviceTypeCapability(apiKey, selectedDeviceType, capability.capabilityId);
      setAddingCompatibility(false);
      setSelectedDeviceType("");
      load();
    } catch (err) {
      handleError(err);
    } finally {
      setSaving(false);
    }
  }

  async function handleRemoveCompatibility(deviceTypeCapabilityId: string) {
    setError(null);

    try {
      await removeDeviceTypeCapability(apiKey, deviceTypeCapabilityId);
      load();
    } catch (err) {
      handleError(err);
    }
  }

  return (
    <div className="confirm-overlay" onClick={onClose}>
      <div
        className="form-dialog"
        role="dialog"
        aria-modal="true"
        aria-label={`${capability.capabilityName} - Relationships`}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="form-field">
          <label className="form-label">{capability.capabilityName} - Dependencies</label>
        </div>

        {error && <p className="form-dialog-error">{error}</p>}

        {!dependencies ? (
          <p>Loading dependencies...</p>
        ) : ownDependencies.length === 0 ? (
          <p className="form-hint">No dependencies - this capability doesn't require any other.</p>
        ) : (
          <div className="entity-list">
            {ownDependencies.map((d) => (
              <div className="entity-row entity-row-static" key={d.dependencyId}>
                <div className="entity-row-main">
                  <div className="entity-row-title">
                    Requires: {capabilityNameById.get(d.dependsOnCapabilityId) ?? d.dependsOnCapabilityId}
                  </div>
                </div>
                <div className="entity-row-actions">
                  <button
                    type="button"
                    className="icon-button icon-button-danger"
                    aria-label={`Remove dependency on ${capabilityNameById.get(d.dependsOnCapabilityId) ?? d.dependsOnCapabilityId}`}
                    onClick={() => handleRemoveDependency(d.dependencyId)}
                  >
                    <TrashIcon />
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}

        {addingDependency ? (
          <div className="form-field">
            <label className="form-label" htmlFor="capability-dependency-select">
              Requires
            </label>
            {dependableCapabilities.length === 0 ? (
              <p className="form-hint">No more capabilities available to depend on.</p>
            ) : (
              <select
                id="capability-dependency-select"
                className="form-select"
                value={selectedDependsOn}
                onChange={(e) => setSelectedDependsOn(e.target.value)}
                autoFocus
              >
                <option value="">Select a capability...</option>
                {dependableCapabilities.map((c) => (
                  <option key={c.capabilityId} value={c.capabilityId}>
                    {c.capabilityName}
                  </option>
                ))}
              </select>
            )}
            <div className="confirm-dialog-actions">
              <button type="button" className="confirm-dialog-cancel" onClick={() => setAddingDependency(false)}>
                Cancel
              </button>
              <button
                type="button"
                className="form-dialog-save"
                disabled={!selectedDependsOn || saving}
                onClick={handleAddDependency}
              >
                Add Dependency
              </button>
            </div>
          </div>
        ) : (
          dependableCapabilities.length > 0 && (
            <button type="button" className="form-kv-add" onClick={() => setAddingDependency(true)}>
              + Add Dependency
            </button>
          )
        )}

        <div className="form-field" style={{ marginTop: "1rem" }}>
          <label className="form-label">{capability.capabilityName} - Compatible Device Types</label>
        </div>

        {!compatibility ? (
          <p>Loading compatibility...</p>
        ) : ownCompatibility.length === 0 ? (
          <p className="form-hint">Not compatible with any DeviceType yet - it can't be assigned to a device.</p>
        ) : (
          <div className="entity-list">
            {ownCompatibility.map((c) => (
              <div className="entity-row entity-row-static" key={c.deviceTypeCapabilityId}>
                <div className="entity-row-main">
                  <div className="entity-row-title">
                    {deviceTypeNameById.get(c.deviceTypeId) ?? c.deviceTypeId}
                  </div>
                </div>
                <div className="entity-row-actions">
                  <button
                    type="button"
                    className="icon-button icon-button-danger"
                    aria-label={`Remove compatibility with ${deviceTypeNameById.get(c.deviceTypeId) ?? c.deviceTypeId}`}
                    onClick={() => handleRemoveCompatibility(c.deviceTypeCapabilityId)}
                  >
                    <TrashIcon />
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}

        {addingCompatibility ? (
          <div className="form-field">
            <label className="form-label" htmlFor="capability-compatibility-select">
              Device Type
            </label>
            {availableDeviceTypes.length === 0 ? (
              <p className="form-hint">No more device types available.</p>
            ) : (
              <select
                id="capability-compatibility-select"
                className="form-select"
                value={selectedDeviceType}
                onChange={(e) => setSelectedDeviceType(e.target.value)}
              >
                <option value="">Select a device type...</option>
                {availableDeviceTypes.map((d) => (
                  <option key={d.deviceTypeId} value={d.deviceTypeId}>
                    {d.deviceTypeName}
                  </option>
                ))}
              </select>
            )}
            <div className="confirm-dialog-actions">
              <button type="button" className="confirm-dialog-cancel" onClick={() => setAddingCompatibility(false)}>
                Cancel
              </button>
              <button
                type="button"
                className="form-dialog-save"
                disabled={!selectedDeviceType || saving}
                onClick={handleAddCompatibility}
              >
                Add Compatibility
              </button>
            </div>
          </div>
        ) : (
          availableDeviceTypes.length > 0 && (
            <button type="button" className="form-kv-add" onClick={() => setAddingCompatibility(true)}>
              + Add Compatible Device Type
            </button>
          )
        )}

        <div className="confirm-dialog-actions" style={{ marginTop: "1rem" }}>
          <button type="button" className="confirm-dialog-cancel" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
