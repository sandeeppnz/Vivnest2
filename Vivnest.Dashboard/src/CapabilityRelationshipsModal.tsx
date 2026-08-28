import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useState } from "react";
import {
  addCapabilityDependency,
  addDeviceTypeCapability,
  removeCapabilityDependency,
  removeDeviceTypeCapability,
  type CapabilityAdmin,
  type CapabilityDependency,
  type DeviceTypeAdmin,
  type DeviceTypeCapability,
} from "./api";
import { ConfirmDialog } from "./ConfirmDialog";
import { TrashIcon } from "./icons";
import { useCapabilityDependencies, useDeviceTypeCapabilities } from "./queries";
import { useApiKey } from "./session";

// What the open remove-confirmation is about - the two lists' rows carry
// different record types, so the target remembers which kind it is.
type RemoveTarget =
  | { kind: "dependency"; dependency: CapabilityDependency }
  | { kind: "compatibility"; compatibility: DeviceTypeCapability };

interface CapabilityRelationshipsModalProps {
  open: boolean;
  capability: CapabilityAdmin | null;
  capabilities: CapabilityAdmin[];
  deviceTypes: DeviceTypeAdmin[];
  onClose: () => void;
}

// Answers "what does this Capability require, and which DeviceTypes can
// use it?" (decision-log.md ADR-062, Phase 5) - two simple lists in one
// modal, each shaped like AgentCapabilitiesModal (plain list +
// Add/Remove, existence is the fact, no ExecutingAgent/Enabled richness
// DeviceCapabilitiesModal needs). Both underlying tables are small and
// global, so both ride the shared query cache whole and are filtered
// client-side to this one Capability. Only direct dependencies are shown
// (spec's own explicit minimum bar) - no transitive-chain rendering.
export function CapabilityRelationshipsModal({
  open,
  capability,
  capabilities,
  deviceTypes,
  onClose,
}: CapabilityRelationshipsModalProps) {
  const apiKey = useApiKey();
  const queryClient = useQueryClient();
  const dependenciesQuery = useCapabilityDependencies(open);
  const compatibilityQuery = useDeviceTypeCapabilities(open);

  const [error, setError] = useState<string | null>(null);
  const [addingDependency, setAddingDependency] = useState(false);
  const [selectedDependsOn, setSelectedDependsOn] = useState("");
  const [addingCompatibility, setAddingCompatibility] = useState(false);
  const [selectedDeviceType, setSelectedDeviceType] = useState("");
  const [removingTarget, setRemovingTarget] = useState<RemoveTarget | null>(null);

  useEffect(() => {
    if (!open || !capability) return;

    setError(null);
    setAddingDependency(false);
    setSelectedDependsOn("");
    setAddingCompatibility(false);
    setSelectedDeviceType("");
    setRemovingTarget(null);
  }, [open, capability]);

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
    () => (dependenciesQuery.data ?? []).filter((d) => d.capabilityId === capability?.capabilityId),
    [dependenciesQuery.data, capability],
  );

  const ownCompatibility = useMemo(
    () => (compatibilityQuery.data ?? []).filter((c) => c.capabilityId === capability?.capabilityId),
    [compatibilityQuery.data, capability],
  );

  const dependableCapabilities = useMemo(() => {
    const already = new Set(ownDependencies.map((d) => d.dependsOnCapabilityId));
    return capabilities.filter((c) => c.capabilityId !== capability?.capabilityId && !already.has(c.capabilityId));
  }, [capabilities, ownDependencies, capability]);

  const availableDeviceTypes = useMemo(() => {
    const already = new Set(ownCompatibility.map((c) => c.deviceTypeId));
    return deviceTypes.filter((d) => !already.has(d.deviceTypeId));
  }, [deviceTypes, ownCompatibility]);

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ["capability-dependencies"] });
    queryClient.invalidateQueries({ queryKey: ["device-type-capabilities"] });
  };

  const addDependencyMutation = useMutation({
    mutationFn: (dependsOnCapabilityId: string) =>
      addCapabilityDependency(apiKey, capability!.capabilityId, dependsOnCapabilityId),
    onSuccess: () => {
      setAddingDependency(false);
      setSelectedDependsOn("");
      invalidate();
    },
    onError: (err) => setError(err.message),
  });

  const addCompatibilityMutation = useMutation({
    mutationFn: (deviceTypeId: string) =>
      addDeviceTypeCapability(apiKey, deviceTypeId, capability!.capabilityId),
    onSuccess: () => {
      setAddingCompatibility(false);
      setSelectedDeviceType("");
      invalidate();
    },
    onError: (err) => setError(err.message),
  });

  const removeMutation = useMutation({
    mutationFn: (target: RemoveTarget) =>
      target.kind === "dependency"
        ? removeCapabilityDependency(apiKey, target.dependency.dependencyId)
        : removeDeviceTypeCapability(apiKey, target.compatibility.deviceTypeCapabilityId),
    onSuccess: invalidate,
    onError: (err) => setError(err.message),
  });

  if (!open || !capability) return null;

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
        {dependenciesQuery.isError && <p className="form-dialog-error">{dependenciesQuery.error.message}</p>}

        {!dependenciesQuery.data ? (
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
                    onClick={() => setRemovingTarget({ kind: "dependency", dependency: d })}
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
                disabled={!selectedDependsOn || addDependencyMutation.isPending}
                onClick={() => {
                  setError(null);
                  addDependencyMutation.mutate(selectedDependsOn);
                }}
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

        {!compatibilityQuery.data ? (
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
                    onClick={() => setRemovingTarget({ kind: "compatibility", compatibility: c })}
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
                disabled={!selectedDeviceType || addCompatibilityMutation.isPending}
                onClick={() => {
                  setError(null);
                  addCompatibilityMutation.mutate(selectedDeviceType);
                }}
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

        <ConfirmDialog
          open={removingTarget !== null}
          message={
            removingTarget === null
              ? ""
              : removingTarget.kind === "dependency"
                ? `Remove ${capability.capabilityName}'s dependency on ${capabilityNameById.get(removingTarget.dependency.dependsOnCapabilityId) ?? removingTarget.dependency.dependsOnCapabilityId}?`
                : `Remove ${capability.capabilityName}'s compatibility with ${deviceTypeNameById.get(removingTarget.compatibility.deviceTypeId) ?? removingTarget.compatibility.deviceTypeId}?`
          }
          confirmLabel="Remove"
          onConfirm={() => {
            const target = removingTarget;
            setRemovingTarget(null);
            setError(null);
            if (target) removeMutation.mutate(target);
          }}
          onCancel={() => setRemovingTarget(null)}
        />
      </div>
    </div>
  );
}
