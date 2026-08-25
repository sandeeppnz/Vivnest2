import { useEffect, useMemo, useState } from "react";
import {
  ApiError,
  assignDeviceCapability,
  getAgentCapabilities,
  getCapabilityDependencies,
  getDeviceCapabilityAssignments,
  getDeviceTypeCapabilities,
  unassignDeviceCapability,
  updateDeviceCapabilityAssignment,
  type AgentRegistry,
  type CapabilityAdmin,
  type CapabilityConfigurationField,
  type CapabilityDependency,
  type DeviceCapabilityAssignment,
  type DeviceRegistry,
  type DeviceTypeCapability,
} from "./api";
import { ConfirmDialog } from "./ConfirmDialog";
import { EditIcon, TrashIcon } from "./icons";

interface DeviceCapabilitiesModalProps {
  open: boolean;
  device: DeviceRegistry | null;
  capabilities: CapabilityAdmin[];
  agents: AgentRegistry[];
  apiKey: string;
  onAuthError: () => void;
  onClose: () => void;
}

// Renders one input per ConfigurationSchema field (decision-log.md
// ADR-062, Phase 5) - text/number/checkbox, a <select> instead of a text
// input when the field declares AllowedValues. Shared between the Add
// form and the per-assignment "Configure" panel so both stay driven by
// the same schema instead of duplicating field-rendering logic.
function ConfigFields({
  schema,
  values,
  onChange,
}: {
  schema: CapabilityConfigurationField[];
  values: Record<string, string>;
  onChange: (name: string, value: string) => void;
}) {
  if (schema.length === 0) return null;

  return (
    <div className="form-schema-list">
      {schema.map((field) => (
        <div className="form-field" key={field.name}>
          <label className="form-label" htmlFor={`config-${field.name}`}>
            {field.name}
            {field.required ? " *" : ""}
          </label>
          {field.type === "Boolean" ? (
            <label className="form-checklist-item">
              <input
                type="checkbox"
                checked={values[field.name] === "true"}
                onChange={(e) => onChange(field.name, e.target.checked ? "true" : "false")}
              />
              {field.name}
            </label>
          ) : field.type === "String" && field.allowedValues && field.allowedValues.length > 0 ? (
            <select
              id={`config-${field.name}`}
              className="form-select"
              value={values[field.name] ?? ""}
              onChange={(e) => onChange(field.name, e.target.value)}
            >
              <option value="">Not set</option>
              {field.allowedValues.map((v) => (
                <option key={v} value={v}>
                  {v}
                </option>
              ))}
            </select>
          ) : (
            <input
              id={`config-${field.name}`}
              type={field.type === "Number" ? "number" : "text"}
              className="form-input"
              value={values[field.name] ?? ""}
              onChange={(e) => onChange(field.name, e.target.value)}
              placeholder={field.defaultValue ?? ""}
              min={field.minimum ?? undefined}
              max={field.maximum ?? undefined}
            />
          )}
        </div>
      ))}
    </div>
  );
}

function defaultsFor(schema: CapabilityConfigurationField[]): Record<string, string> {
  const values: Record<string, string> = {};
  for (const field of schema) {
    if (field.defaultValue != null) values[field.name] = field.defaultValue;
  }
  return values;
}

// Answers "what capabilities are enabled for this Device, and which
// Agent executes them?" (decision-log.md ADR-057/059), extended for
// Phase 5 (ADR-062) to also answer "which of those are actually valid" -
// the Add form's Capability dropdown is filtered to what's compatible
// with this Device's DeviceType, the Executing Agent dropdown stays
// filtered to Agents that declare the selected Capability (unchanged
// from ADR-060), unmet direct dependencies block Assign with an inline
// explanation, and configuration is a dynamic form driven by the
// selected Capability's ConfigurationSchema instead of a bare Enabled
// checkbox. Deliberately NOT symmetrical with AgentCapabilitiesModal -
// an assignment has ExecutingAgent/Enabled/Configuration a plain
// declaration doesn't, so this stays the richer of the two.
export function DeviceCapabilitiesModal({
  open,
  device,
  capabilities,
  agents,
  apiKey,
  onAuthError,
  onClose,
}: DeviceCapabilitiesModalProps) {
  const [assignments, setAssignments] = useState<DeviceCapabilityAssignment[] | null>(null);
  const [agentCapabilityIdsByAgent, setAgentCapabilityIdsByAgent] = useState<Map<string, Set<string>> | null>(null);
  const [deviceTypeCapabilities, setDeviceTypeCapabilities] = useState<DeviceTypeCapability[] | null>(null);
  const [dependencies, setDependencies] = useState<CapabilityDependency[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [selectedCapabilityId, setSelectedCapabilityId] = useState("");
  const [selectedExecutingAgentId, setSelectedExecutingAgentId] = useState("");
  const [newEnabled, setNewEnabled] = useState(true);
  const [newSettings, setNewSettings] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const [configuringId, setConfiguringId] = useState<string | null>(null);
  const [editSettings, setEditSettings] = useState<Record<string, string>>({});
  const [removingTarget, setRemovingTarget] = useState<DeviceCapabilityAssignment | null>(null);

  function handleError(err: unknown) {
    if (err instanceof ApiError && err.status === 401) {
      onAuthError();
      return;
    }

    setError(err instanceof Error ? err.message : "Something went wrong.");
  }

  function load(deviceId: string) {
    setError(null);

    getDeviceCapabilityAssignments(apiKey, deviceId)
      .then((result) => setAssignments(result.filter((a) => a.status === "Active")))
      .catch(handleError);
  }

  useEffect(() => {
    if (!open || !device) return;

    setAssignments(null);
    setAgentCapabilityIdsByAgent(null);
    setDeviceTypeCapabilities(null);
    setDependencies(null);
    setAdding(false);
    setSelectedCapabilityId("");
    setSelectedExecutingAgentId("");
    setNewEnabled(true);
    setNewSettings({});
    setConfiguringId(null);
    setRemovingTarget(null);

    load(device.deviceId);
    getDeviceTypeCapabilities(apiKey).then(setDeviceTypeCapabilities).catch(handleError);
    getCapabilityDependencies(apiKey).then(setDependencies).catch(handleError);

    Promise.all(agents.map(async (a) => [a.agentId, await getAgentCapabilities(apiKey, a.agentId)] as const))
      .then((entries) => {
        const map = new Map<string, Set<string>>();
        for (const [agentId, declarations] of entries) {
          map.set(
            agentId,
            new Set(declarations.filter((d) => d.status === "Active").map((d) => d.capabilityId)),
          );
        }
        setAgentCapabilityIdsByAgent(map);
      })
      .catch(handleError);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, device]);

  useEffect(() => {
    if (!open) return;

    function handleKeyDown(event: KeyboardEvent) {
      // While the remove confirmation is up, Escape belongs to it (its own
      // handler closes it) - without this gate one Escape would close both.
      if (event.key === "Escape" && removingTarget === null) {
        onClose();
      }
    }

    window.addEventListener("keydown", handleKeyDown);

    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [open, onClose, removingTarget]);

  const capabilityById = useMemo(() => {
    const map = new Map<string, CapabilityAdmin>();
    for (const c of capabilities) map.set(c.capabilityId, c);
    return map;
  }, [capabilities]);

  const agentNameById = useMemo(() => {
    const map = new Map<string, string>();
    for (const a of agents) map.set(a.agentId, a.name);
    return map;
  }, [agents]);

  const assignedCapabilityIds = useMemo(
    () => new Set((assignments ?? []).map((a) => a.capabilityId)),
    [assignments],
  );

  // Compatible with this Device's DeviceType (ADR-062) - a capability the
  // system can't answer "is this valid for this DeviceType?" for (no
  // DeviceTypeId set) shows nothing, same as the backend's own AssignAsync
  // rejection reasoning.
  const compatibleCapabilityIds = useMemo(() => {
    if (!device?.deviceTypeId || !deviceTypeCapabilities) return new Set<string>();
    return new Set(
      deviceTypeCapabilities.filter((c) => c.deviceTypeId === device.deviceTypeId).map((c) => c.capabilityId),
    );
  }, [deviceTypeCapabilities, device]);

  const availableCapabilities = useMemo(
    () => capabilities.filter((c) => !assignedCapabilityIds.has(c.capabilityId) && compatibleCapabilityIds.has(c.capabilityId)),
    [capabilities, assignedCapabilityIds, compatibleCapabilityIds],
  );

  // Only Agents that have declared the selected Capability via
  // AgentCapability - agents lacking it simply don't show up, same as
  // the user's own spec: "A001 and A003 shouldn't appear."
  const eligibleAgents = useMemo(() => {
    if (!selectedCapabilityId || !agentCapabilityIdsByAgent) return [];
    return agents.filter((a) => agentCapabilityIdsByAgent.get(a.agentId)?.has(selectedCapabilityId));
  }, [agents, agentCapabilityIdsByAgent, selectedCapabilityId]);

  // Direct dependencies of the selected Capability that this Device
  // doesn't already have actively assigned (ADR-062) - Assign is blocked
  // until these are satisfied, same rule CapabilityAssignmentService
  // enforces server-side.
  const unmetDependencies = useMemo(() => {
    if (!selectedCapabilityId || !dependencies) return [];
    return dependencies
      .filter((d) => d.capabilityId === selectedCapabilityId && !assignedCapabilityIds.has(d.dependsOnCapabilityId))
      .map((d) => capabilityById.get(d.dependsOnCapabilityId)?.capabilityName ?? d.dependsOnCapabilityId);
  }, [selectedCapabilityId, dependencies, assignedCapabilityIds, capabilityById]);

  const selectedCapability = selectedCapabilityId ? capabilityById.get(selectedCapabilityId) : undefined;

  if (!open || !device) return null;

  function selectCapability(capabilityId: string) {
    setSelectedCapabilityId(capabilityId);
    setSelectedExecutingAgentId("");
    const schema = capabilityId ? capabilityById.get(capabilityId)?.configurationSchema ?? [] : [];
    setNewSettings(defaultsFor(schema));
  }

  async function handleAssign() {
    if (!device || !selectedCapabilityId || !selectedExecutingAgentId || unmetDependencies.length > 0) return;

    setSaving(true);
    setError(null);

    try {
      await assignDeviceCapability(
        apiKey,
        device.deviceId,
        selectedCapabilityId,
        selectedExecutingAgentId,
        newEnabled,
        newSettings,
      );
      setAdding(false);
      selectCapability("");
      load(device.deviceId);
    } catch (err) {
      handleError(err);
    } finally {
      setSaving(false);
    }
  }

  async function handleToggleEnabled(a: DeviceCapabilityAssignment) {
    if (!device) return;

    setError(null);

    try {
      await updateDeviceCapabilityAssignment(apiKey, a.deviceCapabilityId, a.executingAgentId, !a.enabled, a.settings);
      load(device.deviceId);
    } catch (err) {
      handleError(err);
    }
  }

  async function handleRemove() {
    if (!device || !removingTarget) return;

    const capabilityId = removingTarget.capabilityId;

    setRemovingTarget(null);
    setError(null);

    try {
      await unassignDeviceCapability(apiKey, device.deviceId, capabilityId);
      load(device.deviceId);
    } catch (err) {
      handleError(err);
    }
  }

  function startConfiguring(a: DeviceCapabilityAssignment) {
    setConfiguringId(a.deviceCapabilityId);
    setEditSettings({ ...a.settings });
  }

  async function handleSaveConfiguration(a: DeviceCapabilityAssignment) {
    if (!device) return;

    setError(null);

    try {
      await updateDeviceCapabilityAssignment(apiKey, a.deviceCapabilityId, a.executingAgentId, a.enabled, editSettings);
      setConfiguringId(null);
      load(device.deviceId);
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
        aria-label={`${device.name} - Capabilities`}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="form-field">
          <label className="form-label">{device.name} - Capabilities</label>
        </div>

        {error && <p className="form-dialog-error">{error}</p>}

        {!assignments ? (
          <p>Loading capabilities...</p>
        ) : assignments.length === 0 ? (
          <p className="form-hint">No capabilities assigned yet.</p>
        ) : (
          <div className="entity-list">
            {assignments.map((a) => (
              <div key={a.deviceCapabilityId}>
                <div className="entity-row entity-row-static">
                  <div className="entity-row-main">
                    <div>
                      <div className="entity-row-title">
                        {capabilityById.get(a.capabilityId)?.capabilityName ?? a.capabilityId}
                      </div>
                      <div className="entity-row-subtitle">
                        Executed by: {a.executingAgentId ? (agentNameById.get(a.executingAgentId) ?? a.executingAgentId) : "Not assigned"}
                      </div>
                    </div>
                  </div>
                  <div className="entity-row-actions">
                    <button
                      type="button"
                      className={`status ${a.enabled ? "status-online" : "status-offline"}`}
                      onClick={() => handleToggleEnabled(a)}
                    >
                      {a.enabled ? "Enabled" : "Disabled"}
                    </button>
                    <button
                      type="button"
                      className="icon-button"
                      aria-label={`Configure ${capabilityById.get(a.capabilityId)?.capabilityName ?? a.capabilityId}`}
                      onClick={() => (configuringId === a.deviceCapabilityId ? setConfiguringId(null) : startConfiguring(a))}
                    >
                      <EditIcon />
                    </button>
                    <button
                      type="button"
                      className="icon-button icon-button-danger"
                      aria-label={`Remove ${capabilityById.get(a.capabilityId)?.capabilityName ?? a.capabilityId}`}
                      onClick={() => setRemovingTarget(a)}
                    >
                      <TrashIcon />
                    </button>
                  </div>
                </div>
                {configuringId === a.deviceCapabilityId && (
                  <div className="form-schema-row">
                    {(capabilityById.get(a.capabilityId)?.configurationSchema ?? []).length === 0 ? (
                      <p className="form-hint">This capability has no configuration.</p>
                    ) : (
                      <ConfigFields
                        schema={capabilityById.get(a.capabilityId)?.configurationSchema ?? []}
                        values={editSettings}
                        onChange={(name, value) => setEditSettings((current) => ({ ...current, [name]: value }))}
                      />
                    )}
                    <div className="confirm-dialog-actions">
                      <button type="button" className="confirm-dialog-cancel" onClick={() => setConfiguringId(null)}>
                        Cancel
                      </button>
                      <button type="button" className="form-dialog-save" onClick={() => handleSaveConfiguration(a)}>
                        Save Configuration
                      </button>
                    </div>
                  </div>
                )}
              </div>
            ))}
          </div>
        )}

        {adding ? (
          <>
            <div className="form-field">
              <label className="form-label" htmlFor="device-capability-select">
                Capability
              </label>
              {!device.deviceTypeId ? (
                <p className="form-hint">
                  This device has no DeviceType set - assign one first (Edit device) to see compatible capabilities.
                </p>
              ) : availableCapabilities.length === 0 ? (
                <p className="form-hint">No more compatible capabilities to add.</p>
              ) : (
                <select
                  id="device-capability-select"
                  className="form-select"
                  value={selectedCapabilityId}
                  onChange={(e) => selectCapability(e.target.value)}
                  autoFocus
                >
                  <option value="">Select a capability...</option>
                  {availableCapabilities.map((c) => (
                    <option key={c.capabilityId} value={c.capabilityId}>
                      {c.capabilityName}
                    </option>
                  ))}
                </select>
              )}
            </div>
            {selectedCapabilityId && unmetDependencies.length > 0 && (
              <p className="form-dialog-error">
                Requires {unmetDependencies.join(", ")} to be enabled first.
              </p>
            )}
            {selectedCapabilityId && unmetDependencies.length === 0 && (
              <div className="form-field">
                <label className="form-label" htmlFor="device-capability-agent">
                  Executing Agent
                </label>
                {eligibleAgents.length === 0 ? (
                  <p className="form-hint">
                    No agents declare this capability yet - assign it to an agent first (Admin &rarr; Agents).
                  </p>
                ) : (
                  <select
                    id="device-capability-agent"
                    className="form-select"
                    value={selectedExecutingAgentId}
                    onChange={(e) => setSelectedExecutingAgentId(e.target.value)}
                  >
                    <option value="">Select an agent...</option>
                    {eligibleAgents.map((a) => (
                      <option key={a.agentId} value={a.agentId}>
                        {a.name}
                      </option>
                    ))}
                  </select>
                )}
              </div>
            )}
            {selectedCapabilityId && unmetDependencies.length === 0 && eligibleAgents.length > 0 && (
              <>
                <div className="form-field">
                  <label className="form-checklist-item">
                    <input
                      type="checkbox"
                      checked={newEnabled}
                      onChange={(e) => setNewEnabled(e.target.checked)}
                    />
                    Enabled
                  </label>
                </div>
                {selectedCapability && selectedCapability.configurationSchema.length > 0 && (
                  <div className="form-field">
                    <label className="form-label">Configuration</label>
                    <ConfigFields
                      schema={selectedCapability.configurationSchema}
                      values={newSettings}
                      onChange={(name, value) => setNewSettings((current) => ({ ...current, [name]: value }))}
                    />
                  </div>
                )}
              </>
            )}
            <div className="confirm-dialog-actions">
              <button type="button" className="confirm-dialog-cancel" onClick={() => setAdding(false)}>
                Cancel
              </button>
              <button
                type="button"
                className="form-dialog-save"
                disabled={!selectedCapabilityId || !selectedExecutingAgentId || unmetDependencies.length > 0 || saving}
                onClick={handleAssign}
              >
                Assign
              </button>
            </div>
          </>
        ) : (
          <div className="confirm-dialog-actions">
            <button type="button" className="confirm-dialog-cancel" onClick={onClose}>
              Close
            </button>
            {availableCapabilities.length > 0 && (
              <button type="button" className="form-dialog-save" onClick={() => setAdding(true)}>
                + Add Capability
              </button>
            )}
          </div>
        )}

        <ConfirmDialog
          open={removingTarget !== null}
          message={
            removingTarget
              ? `Remove ${capabilityById.get(removingTarget.capabilityId)?.capabilityName ?? removingTarget.capabilityId} from ${device.name}? Its configuration for this device will be lost.`
              : ""
          }
          confirmLabel="Remove"
          onConfirm={handleRemove}
          onCancel={() => setRemovingTarget(null)}
        />
      </div>
    </div>
  );
}
