import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useState } from "react";
import {
  assignDeviceCapability,
  unassignDeviceCapability,
  updateDeviceCapabilityAssignment,
  type AgentRegistry,
  type CapabilityAdmin,
  type CapabilityConfigurationField,
  type DeviceCapabilityAssignment,
  type DeviceRegistry,
} from "./api";
import { ConfirmDialog } from "./ConfirmDialog";
import { EditIcon, TrashIcon } from "./icons";
import {
  useAgentCapabilityMap,
  useCapabilityDependencies,
  useDeviceCapabilityAssignments,
  useDeviceTypeCapabilities,
} from "./queries";
import { useApiKey } from "./session";

interface DeviceCapabilitiesModalProps {
  open: boolean;
  device: DeviceRegistry | null;
  capabilities: CapabilityAdmin[];
  agents: AgentRegistry[];
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
  onClose,
}: DeviceCapabilitiesModalProps) {
  const apiKey = useApiKey();
  const queryClient = useQueryClient();

  const assignmentsQuery = useDeviceCapabilityAssignments(open && device ? device.deviceId : null);
  const deviceTypeCapsQuery = useDeviceTypeCapabilities(open);
  const dependenciesQuery = useCapabilityDependencies(open);
  const agentCapabilityMapQuery = useAgentCapabilityMap(agents.map((a) => a.agentId), open);

  const [error, setError] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [selectedCapabilityId, setSelectedCapabilityId] = useState("");
  const [selectedExecutingAgentId, setSelectedExecutingAgentId] = useState("");
  const [newEnabled, setNewEnabled] = useState(true);
  const [newSettings, setNewSettings] = useState<Record<string, string>>({});
  const [configuringId, setConfiguringId] = useState<string | null>(null);
  const [editSettings, setEditSettings] = useState<Record<string, string>>({});
  const [removingTarget, setRemovingTarget] = useState<DeviceCapabilityAssignment | null>(null);

  useEffect(() => {
    if (!open || !device) return;

    setError(null);
    setAdding(false);
    setSelectedCapabilityId("");
    setSelectedExecutingAgentId("");
    setNewEnabled(true);
    setNewSettings({});
    setConfiguringId(null);
    setRemovingTarget(null);
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

  const assignments = useMemo(
    () => assignmentsQuery.data?.filter((a) => a.status === "Active") ?? null,
    [assignmentsQuery.data],
  );

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
    if (!device?.deviceTypeId || !deviceTypeCapsQuery.data) return new Set<string>();
    return new Set(
      deviceTypeCapsQuery.data.filter((c) => c.deviceTypeId === device.deviceTypeId).map((c) => c.capabilityId),
    );
  }, [deviceTypeCapsQuery.data, device]);

  const availableCapabilities = useMemo(
    () => capabilities.filter((c) => !assignedCapabilityIds.has(c.capabilityId) && compatibleCapabilityIds.has(c.capabilityId)),
    [capabilities, assignedCapabilityIds, compatibleCapabilityIds],
  );

  // Only Agents that have declared the selected Capability via
  // AgentCapability - agents lacking it simply don't show up, same as
  // the user's own spec: "A001 and A003 shouldn't appear."
  const eligibleAgents = useMemo(() => {
    if (!selectedCapabilityId || !agentCapabilityMapQuery.data) return [];
    return agents.filter((a) => agentCapabilityMapQuery.data.get(a.agentId)?.has(selectedCapabilityId));
  }, [agents, agentCapabilityMapQuery.data, selectedCapabilityId]);

  // Direct dependencies of the selected Capability that this Device
  // doesn't already have actively assigned (ADR-062) - Assign is blocked
  // until these are satisfied, same rule CapabilityAssignmentService
  // enforces server-side.
  const unmetDependencies = useMemo(() => {
    if (!selectedCapabilityId || !dependenciesQuery.data) return [];
    return dependenciesQuery.data
      .filter((d) => d.capabilityId === selectedCapabilityId && !assignedCapabilityIds.has(d.dependsOnCapabilityId))
      .map((d) => capabilityById.get(d.dependsOnCapabilityId)?.capabilityName ?? d.dependsOnCapabilityId);
  }, [selectedCapabilityId, dependenciesQuery.data, assignedCapabilityIds, capabilityById]);

  const selectedCapability = selectedCapabilityId ? capabilityById.get(selectedCapabilityId) : undefined;

  const invalidateAssignments = () =>
    queryClient.invalidateQueries({ queryKey: ["device-capability-assignments"] });

  const assignMutation = useMutation({
    mutationFn: () =>
      assignDeviceCapability(
        apiKey,
        device!.deviceId,
        selectedCapabilityId,
        selectedExecutingAgentId,
        newEnabled,
        newSettings,
      ),
    onSuccess: () => {
      setAdding(false);
      setSelectedCapabilityId("");
      setSelectedExecutingAgentId("");
      setNewSettings({});
      invalidateAssignments();
    },
    onError: (err) => setError(err.message),
  });

  const updateMutation = useMutation({
    mutationFn: (input: { assignment: DeviceCapabilityAssignment; enabled: boolean; settings: Record<string, string> }) =>
      updateDeviceCapabilityAssignment(
        apiKey,
        input.assignment.deviceCapabilityId,
        input.assignment.executingAgentId,
        input.enabled,
        input.settings,
      ),
    onSuccess: () => {
      setConfiguringId(null);
      invalidateAssignments();
    },
    onError: (err) => setError(err.message),
  });

  const removeMutation = useMutation({
    mutationFn: (capabilityId: string) => unassignDeviceCapability(apiKey, device!.deviceId, capabilityId),
    onSuccess: invalidateAssignments,
    onError: (err) => setError(err.message),
  });

  if (!open || !device) return null;

  function selectCapability(capabilityId: string) {
    setSelectedCapabilityId(capabilityId);
    setSelectedExecutingAgentId("");
    const schema = capabilityId ? capabilityById.get(capabilityId)?.configurationSchema ?? [] : [];
    setNewSettings(defaultsFor(schema));
  }

  function startConfiguring(a: DeviceCapabilityAssignment) {
    setConfiguringId(a.deviceCapabilityId);
    setEditSettings({ ...a.settings });
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
        {assignmentsQuery.isError && <p className="form-dialog-error">{assignmentsQuery.error.message}</p>}

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
                      onClick={() => {
                        setError(null);
                        updateMutation.mutate({ assignment: a, enabled: !a.enabled, settings: a.settings });
                      }}
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
                      <button
                        type="button"
                        className="form-dialog-save"
                        onClick={() => {
                          setError(null);
                          updateMutation.mutate({ assignment: a, enabled: a.enabled, settings: editSettings });
                        }}
                      >
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
                disabled={!selectedCapabilityId || !selectedExecutingAgentId || unmetDependencies.length > 0 || assignMutation.isPending}
                onClick={() => {
                  setError(null);
                  assignMutation.mutate();
                }}
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
          onConfirm={() => {
            const target = removingTarget;
            setRemovingTarget(null);
            setError(null);
            if (target) removeMutation.mutate(target.capabilityId);
          }}
          onCancel={() => setRemovingTarget(null)}
        />
      </div>
    </div>
  );
}
