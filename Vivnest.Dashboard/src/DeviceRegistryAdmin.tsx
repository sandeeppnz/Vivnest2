import { useEffect, useMemo, useState } from "react";
import {
  ApiError,
  createDeviceRegistryEntry,
  deleteDeviceRegistryEntry,
  getAgentRegistry,
  getDeviceRegistry,
  getDeviceTypes,
  updateDeviceRegistryEntry,
  type AgentRegistry,
  type DeviceRegistry,
  type DeviceRegistryFields,
  type DeviceTypeAdmin,
} from "./api";
import { DeviceRegistryFormModal } from "./DeviceRegistryFormModal";
import { ConfirmDialog } from "./ConfirmDialog";
import { EditIcon, TrashIcon } from "./icons";

interface DeviceRegistryAdminProps {
  apiKey: string;
  onAuthError: () => void;
}

// Mirrors AgentRegistryAdmin.tsx's shape (decision-log.md ADR-048) - device
// types and agents are fetched alongside devices purely for client-side
// cross-referencing (id -> name), same "resolve locally, no server-side
// join" convention already established for the row badges and the form's
// dropdowns. Which capabilities a device has is DeviceCapability's job now
// (ADR-057), not shown on this screen.
export function DeviceRegistryAdmin({ apiKey, onAuthError }: DeviceRegistryAdminProps) {
  const [devices, setDevices] = useState<DeviceRegistry[] | null>(null);
  const [deviceTypes, setDeviceTypes] = useState<DeviceTypeAdmin[]>([]);
  const [agents, setAgents] = useState<AgentRegistry[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [editingTarget, setEditingTarget] = useState<DeviceRegistry | "new" | null>(null);
  const [deletingTarget, setDeletingTarget] = useState<DeviceRegistry | null>(null);
  // Separate from `error` above (which is a load failure - replaces the
  // whole page) - a save failure (e.g. the Settings credential guard
  // rejecting a key) shows inline in the still-open modal instead, so a
  // validation error on this 10-field form doesn't wipe everything the
  // user just filled in. Found live: this is exactly what happened when
  // a "Password" key got rejected.
  const [saveError, setSaveError] = useState<string | null>(null);

  function handleError(err: unknown) {
    if (err instanceof ApiError && err.status === 401) {
      onAuthError();
      return;
    }

    setError(err instanceof Error ? err.message : "Something went wrong.");
  }

  function load() {
    setError(null);

    getDeviceRegistry(apiKey)
      .then(setDevices)
      .catch(handleError);
  }

  useEffect(() => {
    let cancelled = false;

    setDevices(null);
    setError(null);

    getDeviceRegistry(apiKey)
      .then((result) => !cancelled && setDevices(result))
      .catch((err) => !cancelled && handleError(err));

    getDeviceTypes(apiKey)
      .then((result) => !cancelled && setDeviceTypes(result))
      .catch((err) => !cancelled && handleError(err));

    getAgentRegistry(apiKey)
      .then((result) => !cancelled && setAgents(result))
      .catch((err) => !cancelled && handleError(err));

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiKey]);

  const deviceTypeNameById = useMemo(() => {
    const map = new Map<string, string>();
    for (const d of deviceTypes) map.set(d.deviceTypeId, d.deviceTypeName);
    return map;
  }, [deviceTypes]);

  const agentNameById = useMemo(() => {
    const map = new Map<string, string>();
    for (const a of agents) map.set(a.agentId, a.name);
    return map;
  }, [agents]);

  const filtered = useMemo(() => {
    if (!devices) return [];
    if (!search.trim()) return devices;

    const query = search.trim().toLowerCase();
    return devices.filter((d) => d.name.toLowerCase().includes(query));
  }, [devices, search]);

  async function handleSave(fields: DeviceRegistryFields) {
    setSaveError(null);

    try {
      if (editingTarget === "new") {
        await createDeviceRegistryEntry(apiKey, fields);
      } else if (editingTarget) {
        await updateDeviceRegistryEntry(apiKey, editingTarget.deviceId, fields);
      }

      setEditingTarget(null);
      load();
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        onAuthError();
        return;
      }

      setSaveError(err instanceof Error ? err.message : "Something went wrong.");
    }
  }

  async function handleDelete() {
    if (!deletingTarget) return;

    try {
      await deleteDeviceRegistryEntry(apiKey, deletingTarget.deviceId);
      setDeletingTarget(null);
      load();
    } catch (err) {
      setDeletingTarget(null);
      handleError(err);
    }
  }

  if (error) return <p className="error">{error}</p>;
  if (!devices) return <p>Loading devices...</p>;

  return (
    <>
      <div className="list-toolbar">
        <input
          type="text"
          className="list-search"
          placeholder="Filter by name..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <button
          type="button"
          className="form-dialog-save"
          onClick={() => {
            setSaveError(null);
            setEditingTarget("new");
          }}
        >
          + Add
        </button>
      </div>

      {filtered.length === 0 ? (
        <p>No devices registered yet.</p>
      ) : (
        <div className="entity-list">
          {filtered.map((d) => (
            <div className="entity-row entity-row-static" key={d.deviceId}>
              <div className="entity-row-main">
                <div>
                  <div className="entity-row-title">{d.name}</div>
                  <div className="entity-row-subtitle" style={{ fontFamily: "monospace" }}>
                    {d.deviceId}
                  </div>
                  <div className="entity-row-subtitle">
                    {deviceTypeNameById.get(d.deviceTypeId) ?? "No type set"}
                    {d.owningAgentId && ` · ${agentNameById.get(d.owningAgentId) ?? d.owningAgentId}`}
                    {d.location && ` · ${d.location}`}
                  </div>
                </div>
              </div>
              <div className="entity-row-actions">
                <span className={`status ${d.enabled ? "status-online" : "status-unknown"}`}>
                  {d.enabled ? "Enabled" : "Disabled"}
                </span>
                <button
                  type="button"
                  className="icon-button"
                  aria-label={`Edit ${d.name}`}
                  onClick={() => {
                    setSaveError(null);
                    setEditingTarget(d);
                  }}
                >
                  <EditIcon />
                </button>
                <button
                  type="button"
                  className="icon-button icon-button-danger"
                  aria-label={`Delete ${d.name}`}
                  onClick={() => setDeletingTarget(d)}
                >
                  <TrashIcon />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      <DeviceRegistryFormModal
        open={editingTarget !== null}
        initial={editingTarget === "new" ? null : editingTarget}
        deviceTypes={deviceTypes}
        agents={agents}
        error={saveError}
        onSave={handleSave}
        onCancel={() => {
          setSaveError(null);
          setEditingTarget(null);
        }}
      />

      <ConfirmDialog
        open={deletingTarget !== null}
        message={`Delete device "${deletingTarget?.name}"?`}
        confirmLabel="Delete"
        onConfirm={handleDelete}
        onCancel={() => setDeletingTarget(null)}
      />
    </>
  );
}
