import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useState } from "react";
import {
  assignAgentCapability,
  unassignAgentCapability,
  type AgentCapability,
  type AgentRegistry,
  type CapabilityAdmin,
} from "./api";
import { ConfirmDialog } from "./ConfirmDialog";
import { TrashIcon } from "./icons";
import { useAgentCapabilityDeclarations } from "./queries";
import { useApiKey } from "./session";

interface AgentCapabilitiesModalProps {
  open: boolean;
  agent: AgentRegistry | null;
  capabilities: CapabilityAdmin[];
  onClose: () => void;
}

// Answers "what can this Agent do?" (decision-log.md ADR-059) - a real
// declared-capability manifest, not the flat AgentRegistryEntity.CapabilityIds
// list this replaced. Deliberately NOT symmetrical with
// DeviceCapabilitiesModal - a declaration has nothing beyond
// Assign/Unassign (no ExecutingAgent, no Enabled/Disabled, no
// per-assignment config), so this stays a simple list, not the richer
// table the Device side needs.
export function AgentCapabilitiesModal({
  open,
  agent,
  capabilities,
  onClose,
}: AgentCapabilitiesModalProps) {
  const apiKey = useApiKey();
  const queryClient = useQueryClient();
  const declarationsQuery = useAgentCapabilityDeclarations(open && agent ? agent.agentId : null);

  const [error, setError] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [selectedCapabilityId, setSelectedCapabilityId] = useState("");
  const [removingTarget, setRemovingTarget] = useState<AgentCapability | null>(null);

  useEffect(() => {
    if (!open || !agent) return;

    setError(null);
    setAdding(false);
    setSelectedCapabilityId("");
    setRemovingTarget(null);
  }, [open, agent]);

  useEffect(() => {
    if (!open) return;

    function handleKeyDown(event: KeyboardEvent) {
      // Escape belongs to the remove confirmation while it's up - see
      // DeviceCapabilitiesModal's identical gate.
      if (event.key === "Escape" && removingTarget === null) {
        onClose();
      }
    }

    window.addEventListener("keydown", handleKeyDown);

    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [open, onClose, removingTarget]);

  const assignments = useMemo(
    () => declarationsQuery.data?.filter((a) => a.status === "Active") ?? null,
    [declarationsQuery.data],
  );

  const capabilityNameById = useMemo(() => {
    const map = new Map<string, string>();
    for (const c of capabilities) map.set(c.capabilityId, c.capabilityName);
    return map;
  }, [capabilities]);

  const assignedCapabilityIds = useMemo(
    () => new Set((assignments ?? []).map((a) => a.capabilityId)),
    [assignments],
  );

  const availableCapabilities = useMemo(
    () => capabilities.filter((c) => !assignedCapabilityIds.has(c.capabilityId)),
    [capabilities, assignedCapabilityIds],
  );

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ["agent-capability-declarations"] });
    // DeviceCapabilitiesModal's eligible-agent map reads declarations too.
    queryClient.invalidateQueries({ queryKey: ["agent-capability-map"] });
  };

  const assignMutation = useMutation({
    mutationFn: (capabilityId: string) => assignAgentCapability(apiKey, agent!.agentId, capabilityId),
    onSuccess: () => {
      setAdding(false);
      setSelectedCapabilityId("");
      invalidate();
    },
    onError: (err) => setError(err.message),
  });

  const removeMutation = useMutation({
    mutationFn: (capabilityId: string) => unassignAgentCapability(apiKey, agent!.agentId, capabilityId),
    onSuccess: invalidate,
    onError: (err) => setError(err.message),
  });

  if (!open || !agent) return null;

  return (
    <div className="confirm-overlay" onClick={onClose}>
      <div
        className="form-dialog"
        role="dialog"
        aria-modal="true"
        aria-label={`${agent.name} - Capabilities`}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="form-field">
          <label className="form-label">{agent.name} - Capabilities</label>
        </div>

        {error && <p className="form-dialog-error">{error}</p>}
        {declarationsQuery.isError && <p className="form-dialog-error">{declarationsQuery.error.message}</p>}

        {!assignments ? (
          <p>Loading capabilities...</p>
        ) : assignments.length === 0 ? (
          <p className="form-hint">No capabilities declared yet.</p>
        ) : (
          <div className="entity-list">
            {assignments.map((a) => (
              <div className="entity-row entity-row-static" key={a.agentCapabilityId}>
                <div className="entity-row-main">
                  <div className="entity-row-title">
                    {capabilityNameById.get(a.capabilityId) ?? a.capabilityId}
                  </div>
                </div>
                <div className="entity-row-actions">
                  <button
                    type="button"
                    className="icon-button icon-button-danger"
                    aria-label={`Remove ${capabilityNameById.get(a.capabilityId) ?? a.capabilityId}`}
                    onClick={() => setRemovingTarget(a)}
                  >
                    <TrashIcon />
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}

        {adding ? (
          <div className="form-field">
            <label className="form-label" htmlFor="agent-capability-select">
              Capability
            </label>
            {availableCapabilities.length === 0 ? (
              <p className="form-hint">No more capabilities to add.</p>
            ) : (
              <select
                id="agent-capability-select"
                className="form-select"
                value={selectedCapabilityId}
                onChange={(e) => setSelectedCapabilityId(e.target.value)}
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
            <div className="confirm-dialog-actions">
              <button type="button" className="confirm-dialog-cancel" onClick={() => setAdding(false)}>
                Cancel
              </button>
              <button
                type="button"
                className="form-dialog-save"
                disabled={!selectedCapabilityId || assignMutation.isPending}
                onClick={() => {
                  setError(null);
                  assignMutation.mutate(selectedCapabilityId);
                }}
              >
                Assign
              </button>
            </div>
          </div>
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
              ? `Remove the ${capabilityNameById.get(removingTarget.capabilityId) ?? removingTarget.capabilityId} declaration from ${agent.name}?`
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
