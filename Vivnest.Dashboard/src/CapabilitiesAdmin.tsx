import { useEffect, useMemo, useState } from "react";
import {
  ApiError,
  createCapability,
  deleteCapability,
  getCapabilities,
  getDeviceTypes,
  updateCapability,
  type CapabilityAdmin,
  type CapabilityConfigurationField,
  type CapabilityStatus,
  type CapabilityType,
  type DeviceTypeAdmin,
} from "./api";
import { CapabilityFormModal } from "./CapabilityFormModal";
import { CapabilityRelationshipsModal } from "./CapabilityRelationshipsModal";
import { ConfirmDialog } from "./ConfirmDialog";
import { EditIcon, LinkIcon, TrashIcon } from "./icons";

interface CapabilitiesAdminProps {
  apiKey: string;
  onAuthError: () => void;
}

const TYPE_LABELS: Record<CapabilityType, string> = {
  Device: "Device",
  Service: "Service",
  System: "System",
};

const TYPE_STATUS_CLASS: Record<CapabilityType, string> = {
  Device: "status-online",
  Service: "status-accent",
  System: "status-unknown",
};

const STATUS_CLASS: Record<CapabilityStatus, string> = {
  Active: "status-online",
  Retired: "status-offline",
};

export function CapabilitiesAdmin({ apiKey, onAuthError }: CapabilitiesAdminProps) {
  const [capabilities, setCapabilities] = useState<CapabilityAdmin[] | null>(null);
  const [deviceTypes, setDeviceTypes] = useState<DeviceTypeAdmin[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [editingTarget, setEditingTarget] = useState<CapabilityAdmin | "new" | null>(null);
  const [deletingTarget, setDeletingTarget] = useState<CapabilityAdmin | null>(null);
  const [relationshipsTarget, setRelationshipsTarget] = useState<CapabilityAdmin | null>(null);

  function handleError(err: unknown) {
    if (err instanceof ApiError && err.status === 401) {
      onAuthError();
      return;
    }

    setError(err instanceof Error ? err.message : "Something went wrong.");
  }

  function load() {
    setError(null);

    getCapabilities(apiKey)
      .then(setCapabilities)
      .catch(handleError);
  }

  useEffect(() => {
    let cancelled = false;

    setCapabilities(null);
    setError(null);

    getCapabilities(apiKey)
      .then((result) => !cancelled && setCapabilities(result))
      .catch((err) => !cancelled && handleError(err));

    getDeviceTypes(apiKey)
      .then((result) => !cancelled && setDeviceTypes(result))
      .catch((err) => !cancelled && handleError(err));

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiKey]);

  const filtered = useMemo(() => {
    if (!capabilities) return [];
    if (!search.trim()) return capabilities;

    const query = search.trim().toLowerCase();
    return capabilities.filter((c) => c.capabilityName.toLowerCase().includes(query));
  }, [capabilities, search]);

  async function handleSave(
    name: string,
    type: CapabilityType,
    status: CapabilityStatus,
    configurationSchema: CapabilityConfigurationField[],
    configurationSchemaVersion: number,
  ) {
    try {
      if (editingTarget === "new") {
        await createCapability(apiKey, name, type, configurationSchema, configurationSchemaVersion, {});
      } else if (editingTarget) {
        await updateCapability(
          apiKey,
          editingTarget.capabilityId,
          name,
          type,
          status,
          configurationSchema,
          configurationSchemaVersion,
          {},
        );
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
      await deleteCapability(apiKey, deletingTarget.capabilityId);
      setDeletingTarget(null);
      load();
    } catch (err) {
      setDeletingTarget(null);
      handleError(err);
    }
  }

  if (error) return <p className="error">{error}</p>;
  if (!capabilities) return <p>Loading capabilities...</p>;

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
        <p>No capabilities yet.</p>
      ) : (
        <div className="entity-list">
          {filtered.map((c) => (
            <div className="entity-row entity-row-static" key={c.capabilityId}>
              <div className="entity-row-main">
                <div>
                  <div className="entity-row-title">{c.capabilityName}</div>
                  <div className="entity-row-subtitle" style={{ fontFamily: "monospace" }}>
                    {c.capabilityId}
                  </div>
                </div>
              </div>
              <div className="entity-row-actions">
                <span className={`status ${STATUS_CLASS[c.status]}`}>{c.status}</span>
                <span className={`status ${TYPE_STATUS_CLASS[c.capabilityType]}`}>
                  {TYPE_LABELS[c.capabilityType]}
                </span>
                <button
                  type="button"
                  className="icon-button"
                  aria-label={`Manage relationships for ${c.capabilityName}`}
                  onClick={() => setRelationshipsTarget(c)}
                >
                  <LinkIcon />
                </button>
                <button
                  type="button"
                  className="icon-button"
                  aria-label={`Edit ${c.capabilityName}`}
                  onClick={() => setEditingTarget(c)}
                >
                  <EditIcon />
                </button>
                <button
                  type="button"
                  className="icon-button icon-button-danger"
                  aria-label={`Delete ${c.capabilityName}`}
                  onClick={() => setDeletingTarget(c)}
                >
                  <TrashIcon />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      <CapabilityFormModal
        open={editingTarget !== null}
        initial={editingTarget === "new" ? null : editingTarget}
        onSave={handleSave}
        onCancel={() => setEditingTarget(null)}
      />

      <CapabilityRelationshipsModal
        open={relationshipsTarget !== null}
        capability={relationshipsTarget}
        capabilities={capabilities}
        deviceTypes={deviceTypes}
        apiKey={apiKey}
        onAuthError={onAuthError}
        onClose={() => setRelationshipsTarget(null)}
      />

      <ConfirmDialog
        open={deletingTarget !== null}
        message={`Delete capability "${deletingTarget?.capabilityName}"?`}
        confirmLabel="Delete"
        onConfirm={handleDelete}
        onCancel={() => setDeletingTarget(null)}
      />
    </>
  );
}
