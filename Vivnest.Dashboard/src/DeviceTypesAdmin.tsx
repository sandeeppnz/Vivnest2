import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import {
  createDeviceType,
  deleteDeviceType,
  updateDeviceType,
  type DeviceTypeAdmin,
  type DeviceTypeStatus,
} from "./api";
import { ErrorState } from "./ErrorState";
import { DeviceTypeFormModal } from "./DeviceTypeFormModal";
import { ConfirmDialog } from "./ConfirmDialog";
import { EditIcon, TrashIcon } from "./icons";
import { useDeviceTypeCatalogue } from "./queries";
import { useApiKey } from "./session";

// Mirrors CapabilitiesAdmin.tsx exactly, minus the Type badge/dropdown -
// see that file for the reasoning behind this shape.
export function DeviceTypesAdmin() {
  const apiKey = useApiKey();
  const queryClient = useQueryClient();
  const typesQuery = useDeviceTypeCatalogue();

  const [search, setSearch] = useState("");
  const [editingTarget, setEditingTarget] = useState<DeviceTypeAdmin | "new" | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [deletingTarget, setDeletingTarget] = useState<DeviceTypeAdmin | null>(null);
  // A failed delete shows here instead of replacing the whole screen -
  // the list is still perfectly good data.
  const [actionError, setActionError] = useState<string | null>(null);

  const filtered = useMemo(() => {
    const deviceTypes = typesQuery.data;
    if (!deviceTypes) return [];
    if (!search.trim()) return deviceTypes;

    const query = search.trim().toLowerCase();
    return deviceTypes.filter((d) => d.deviceTypeName.toLowerCase().includes(query));
  }, [typesQuery.data, search]);

  const saveMutation = useMutation({
    mutationFn: async (input: { name: string; description: string; status: DeviceTypeStatus }) => {
      if (editingTarget === "new") {
        await createDeviceType(apiKey, input.name, input.description);
      } else if (editingTarget) {
        await updateDeviceType(apiKey, editingTarget.deviceTypeId, input.name, input.description, input.status);
      }
    },
    onSuccess: () => {
      setEditingTarget(null);
      queryClient.invalidateQueries({ queryKey: ["device-type-catalogue"] });
    },
    onError: (err) => setSaveError(err.message),
  });

  const deleteMutation = useMutation({
    mutationFn: (target: DeviceTypeAdmin) => deleteDeviceType(apiKey, target.deviceTypeId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["device-type-catalogue"] }),
    onError: (err) => setActionError(err.message),
    onSettled: () => setDeletingTarget(null),
  });

  if (typesQuery.isError) {
    return <ErrorState message={typesQuery.error.message} onRetry={() => typesQuery.refetch()} />;
  }
  if (!typesQuery.data) return <p>Loading device types...</p>;

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
        <button type="button" className="form-dialog-save" onClick={() => { setSaveError(null); setEditingTarget("new"); }}>
          + Add
        </button>
      </div>

      {actionError && <p className="error">{actionError}</p>}

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
                  onClick={() => { setSaveError(null); setEditingTarget(d); }}
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
        error={saveError}
        onSave={(name, description, status) => {
          setSaveError(null);
          saveMutation.mutate({ name, description, status });
        }}
        onCancel={() => {
          setSaveError(null);
          setEditingTarget(null);
        }}
      />

      <ConfirmDialog
        open={deletingTarget !== null}
        message={`Delete device type "${deletingTarget?.deviceTypeName}"?`}
        confirmLabel="Delete"
        onConfirm={() => {
          setActionError(null);
          if (deletingTarget) deleteMutation.mutate(deletingTarget);
        }}
        onCancel={() => setDeletingTarget(null)}
      />
    </>
  );
}
