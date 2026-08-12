import { useEffect, useMemo, useState } from "react";
import {
  ApiError,
  createDeviceType,
  deleteDeviceType,
  getDeviceTypes,
  updateDeviceType,
  type DeviceTypeAdmin,
  type DeviceTypeStatus,
} from "./api";
import { DeviceTypeFormModal } from "./DeviceTypeFormModal";
import { ConfirmDialog } from "./ConfirmDialog";
import { EditIcon, TrashIcon } from "./icons";

interface DeviceTypesAdminProps {
  apiKey: string;
  onAuthError: () => void;
}

// Mirrors CapabilitiesAdmin.tsx exactly, minus the Type badge/dropdown -
// see that file for the reasoning behind this shape.
export function DeviceTypesAdmin({ apiKey, onAuthError }: DeviceTypesAdminProps) {
  const [deviceTypes, setDeviceTypes] = useState<DeviceTypeAdmin[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [editingTarget, setEditingTarget] = useState<DeviceTypeAdmin | "new" | null>(null);
  const [deletingTarget, setDeletingTarget] = useState<DeviceTypeAdmin | null>(null);

  function handleError(err: unknown) {
    if (err instanceof ApiError && err.status === 401) {
      onAuthError();
      return;
    }

    setError(err instanceof Error ? err.message : "Something went wrong.");
  }

  function load() {
    setError(null);

    getDeviceTypes(apiKey)
      .then(setDeviceTypes)
      .catch(handleError);
  }

  useEffect(() => {
    let cancelled = false;

    setDeviceTypes(null);
    setError(null);

    getDeviceTypes(apiKey)
      .then((result) => !cancelled && setDeviceTypes(result))
      .catch((err) => !cancelled && handleError(err));

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiKey]);

  const filtered = useMemo(() => {
    if (!deviceTypes) return [];
    if (!search.trim()) return deviceTypes;

    const query = search.trim().toLowerCase();
    return deviceTypes.filter((d) => d.deviceTypeName.toLowerCase().includes(query));
  }, [deviceTypes, search]);

  async function handleSave(name: string, description: string, status: DeviceTypeStatus) {
    try {
      if (editingTarget === "new") {
        await createDeviceType(apiKey, name, description);
      } else if (editingTarget) {
        await updateDeviceType(apiKey, editingTarget.deviceTypeId, name, description, status);
      }

      setEditingTarget(null);
      load();
    } catch (err) {
      handleError(err);
    }
  }

  async function handleDelete() {
    if (!deletingTarget) return;

    try {
      await deleteDeviceType(apiKey, deletingTarget.deviceTypeId);
      setDeletingTarget(null);
      load();
    } catch (err) {
      setDeletingTarget(null);
      handleError(err);
    }
  }

  if (error) return <p className="error">{error}</p>;
  if (!deviceTypes) return <p>Loading device types...</p>;

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
        <button type="button" className="form-dialog-save" onClick={() => setEditingTarget("new")}>
          + Add
        </button>
      </div>

      {filtered.length === 0 ? (
        <p>No device types yet.</p>
      ) : (
        <div className="entity-list">
          {filtered.map((d) => (
            <div className="entity-row entity-row-static" key={d.deviceTypeId}>
              <div className="entity-row-main">
                <div>
                  <div className="entity-row-title">{d.deviceTypeName}</div>
                  <div className="entity-row-subtitle" style={{ fontFamily: "monospace" }}>
                    {d.deviceTypeId}
                  </div>
                  {d.description && <div className="entity-row-subtitle">{d.description}</div>}
                </div>
              </div>
              <div className="entity-row-actions">
                <span className={`status ${d.status === "Active" ? "status-online" : "status-offline"}`}>
                  {d.status}
                </span>
                <button
                  type="button"
                  className="icon-button"
                  aria-label={`Edit ${d.deviceTypeName}`}
                  onClick={() => setEditingTarget(d)}
                >
                  <EditIcon />
                </button>
                <button
                  type="button"
                  className="icon-button icon-button-danger"
                  aria-label={`Delete ${d.deviceTypeName}`}
                  onClick={() => setDeletingTarget(d)}
                >
                  <TrashIcon />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      <DeviceTypeFormModal
        open={editingTarget !== null}
        initial={editingTarget === "new" ? null : editingTarget}
        onSave={handleSave}
        onCancel={() => setEditingTarget(null)}
      />

      <ConfirmDialog
        open={deletingTarget !== null}
        message={`Delete device type "${deletingTarget?.deviceTypeName}"?`}
        confirmLabel="Delete"
        onConfirm={handleDelete}
        onCancel={() => setDeletingTarget(null)}
      />
    </>
  );
}
