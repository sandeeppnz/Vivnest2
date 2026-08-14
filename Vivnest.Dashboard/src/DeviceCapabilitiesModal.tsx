import { useEffect, useMemo, useState } from "react";
import {
  ApiError,
  assignDeviceCapability,
  getAgentCapabilities,
  getDeviceCapabilityAssignments,
  unassignDeviceCapability,
  updateDeviceCapabilityAssignment,
  type AgentRegistry,
  type CapabilityAdmin,
  type DeviceCapabilityAssignment,
  type DeviceRegistry,
} from "./api";
import { TrashIcon } from "./icons";

interface DeviceCapabilitiesModalProps {
  open: boolean;
  device: DeviceRegistry | null;
  capabilities: CapabilityAdmin[];
  agents: AgentRegistry[];
  apiKey: string;
  onAuthError: () => void;
  onClose: () => void;
}

// Answers "what capabilities are enabled for this Device, and which
// Agent executes them?" (decision-log.md ADR-057/059). Deliberately NOT
// symmetrical with AgentCapabilitiesModal - an assignment has an
// ExecutingAgent and an Enabled/Disabled state a plain declaration
// doesn't, so this is the richer of the two. The Executing Agent picker
// is filtered to only Agents that have actually declared the selected
// Capability via AgentCapability (fetched per-agent via Promise.all,
// same O(N)-at-this-scale reasoning AgentInstallationsAdmin.tsx already
// uses) - this is the UI surfacing the real validation rule
// CapabilityAssignmentService.IsValidExecutingAgentAsync enforces
// server-side, not just a client-side convenience.
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
  const [error, setError] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [selectedCapabilityId, setSelectedCapabilityId] = useState("");
  const [selectedExecutingAgentId, setSelectedExecutingAgentId] = useState("");
  const [newEnabled, setNewEnabled] = useState(true);
  const [saving, setSaving] = useState(false);

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
    setAdding(false);
    setSelectedCapabilityId("");
    setSelectedExecutingAgentId("");
    setNewEnabled(true);

    load(device.deviceId);

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

  const agentNameById = useMemo(() => {
    const map = new Map<string, string>();
    for (const a of agents) map.set(a.agentId, a.name);
    return map;
  }, [agents]);

  const assignedCapabilityIds = useMemo(
    () => new Set((assignments ?? []).map((a) => a.capabilityId)),
    [assignments],
  );

  const availableCapabilities = useMemo(
    () => capabilities.filter((c) => !assignedCapabilityIds.has(c.capabilityId)),
    [capabilities, assignedCapabilityIds],
  );

  // Only Agents that have declared the selected Capability via
  // AgentCapability - agents lacking it simply don't show up, same as
  // the user's own spec: "A001 and A003 shouldn't appear."
  const eligibleAgents = useMemo(() => {
    if (!selectedCapabilityId || !agentCapabilityIdsByAgent) return [];
    return agents.filter((a) => agentCapabilityIdsByAgent.get(a.agentId)?.has(selectedCapabilityId));
  }, [agents, agentCapabilityIdsByAgent, selectedCapabilityId]);

  if (!open || !device) return null;

  async function handleAssign() {
    if (!device || !selectedCapabilityId || !selectedExecutingAgentId) return;

    setSaving(true);
    setError(null);

    try {
      await assignDeviceCapability(apiKey, device.deviceId, selectedCapabilityId, selectedExecutingAgentId, newEnabled);
      setAdding(false);
      setSelectedCapabilityId("");
      setSelectedExecutingAgentId("");
      setNewEnabled(true);
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
      await updateDeviceCapabilityAssignment(apiKey, a.deviceCapabilityId, a.executingAgentId, !a.enabled);
      load(device.deviceId);
    } catch (err) {
      handleError(err);
    }
  }

  async function handleRemove(capabilityId: string) {
    if (!device) return;

    setError(null);

    try {
      await unassignDeviceCapability(apiKey, device.deviceId, capabilityId);
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
              <div className="entity-row entity-row-static" key={a.deviceCapabilityId}>
                <div className="entity-row-main">
                  <div>
                    <div className="entity-row-title">
                      {capabilityNameById.get(a.capabilityId) ?? a.capabilityId}
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
                    className="icon-button icon-button-danger"
                    aria-label={`Remove ${capabilityNameById.get(a.capabilityId) ?? a.capabilityId}`}
                    onClick={() => handleRemove(a.capabilityId)}
                  >
                    <TrashIcon />
                  </button>
                </div>
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
              {availableCapabilities.length === 0 ? (
                <p className="form-hint">No more capabilities to add.</p>
              ) : (
                <select
                  id="device-capability-select"
                  className="form-select"
                  value={selectedCapabilityId}
                  onChange={(e) => {
                    setSelectedCapabilityId(e.target.value);
                    setSelectedExecutingAgentId("");
                  }}
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
            {selectedCapabilityId && (
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
            {selectedCapabilityId && eligibleAgents.length > 0 && (
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
            )}
            <div className="confirm-dialog-actions">
              <button type="button" className="confirm-dialog-cancel" onClick={() => setAdding(false)}>
                Cancel
              </button>
              <button
                type="button"
                className="form-dialog-save"
                disabled={!selectedCapabilityId || !selectedExecutingAgentId || saving}
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
      </div>
    </div>
  );
}
