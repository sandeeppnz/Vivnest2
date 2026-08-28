import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import {
  createCapability,
  deleteCapability,
  updateCapability,
  type CapabilityAdmin,
  type CapabilityConfigurationField,
  type CapabilityStatus,
  type CapabilityType,
} from "./api";
import { ErrorState } from "./ErrorState";
import { CapabilityFormModal } from "./CapabilityFormModal";
import { CapabilityRelationshipsModal } from "./CapabilityRelationshipsModal";
import { ConfirmDialog } from "./ConfirmDialog";
import { EditIcon, LinkIcon, TrashIcon } from "./icons";
import { useCapabilityCatalogue, useDeviceTypeCatalogue } from "./queries";
import { useApiKey } from "./session";

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

interface SaveInput {
  name: string;
  type: CapabilityType;
  status: CapabilityStatus;
  configurationSchema: CapabilityConfigurationField[];
  configurationSchemaVersion: number;
}

export function CapabilitiesAdmin() {
  const apiKey = useApiKey();
  const queryClient = useQueryClient();
  const catalogueQuery = useCapabilityCatalogue();
  const deviceTypesQuery = useDeviceTypeCatalogue();

  const [search, setSearch] = useState("");
  const [editingTarget, setEditingTarget] = useState<CapabilityAdmin | "new" | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [deletingTarget, setDeletingTarget] = useState<CapabilityAdmin | null>(null);
  const [relationshipsTarget, setRelationshipsTarget] = useState<CapabilityAdmin | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const capabilities = catalogueQuery.data ?? null;

  const filtered = useMemo(() => {
    if (!capabilities) return [];
    if (!search.trim()) return capabilities;

    const query = search.trim().toLowerCase();
    return capabilities.filter((c) => c.capabilityName.toLowerCase().includes(query));
  }, [capabilities, search]);

  const saveMutation = useMutation({
    mutationFn: async (input: SaveInput) => {
      if (editingTarget === "new") {
        await createCapability(apiKey, input.name, input.type, input.configurationSchema, input.configurationSchemaVersion, {});
      } else if (editingTarget) {
        await updateCapability(
          apiKey,
          editingTarget.capabilityId,
          input.name,
          input.type,
          input.status,
          input.configurationSchema,
          input.configurationSchemaVersion,
          // The form doesn't edit DefaultConfiguration (deliberately - see
          // CapabilityFormModal), but the PUT replaces the whole record and
          // the domain treats {} as "set it to empty", not "keep it" - so
          // pass the existing value through, or editing a capability's
          // name would silently erase whatever an API caller had set.
          editingTarget.defaultConfiguration,
        );
      }
    },
    onSuccess: () => {
      setEditingTarget(null);
      queryClient.invalidateQueries({ queryKey: ["capability-catalogue"] });
    },
    onError: (err) => setSaveError(err.message),
  });

  const deleteMutation = useMutation({
    mutationFn: (target: CapabilityAdmin) => deleteCapability(apiKey, target.capabilityId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["capability-catalogue"] }),
    onError: (err) => setActionError(err.message),
    onSettled: () => setDeletingTarget(null),
  });

  if (catalogueQuery.isError) {
    return <ErrorState message={catalogueQuery.error.message} onRetry={() => catalogueQuery.refetch()} />;
  }
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
        <button type="button" className="form-dialog-save" onClick={() => { setSaveError(null); setEditingTarget("new"); }}>
          + Add
        </button>
      </div>

      {actionError && <p className="error">{actionError}</p>}

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
                  onClick={() => { setSaveError(null); setEditingTarget(c); }}
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
        error={saveError}
        onSave={(name, type, status, configurationSchema, configurationSchemaVersion) => {
          setSaveError(null);
          saveMutation.mutate({ name, type, status, configurationSchema, configurationSchemaVersion });
        }}
        onCancel={() => {
          setSaveError(null);
          setEditingTarget(null);
        }}
      />

      <CapabilityRelationshipsModal
        open={relationshipsTarget !== null}
        capability={relationshipsTarget}
        capabilities={capabilities}
        deviceTypes={deviceTypesQuery.data ?? []}
        onClose={() => setRelationshipsTarget(null)}
      />

      <ConfirmDialog
        open={deletingTarget !== null}
        message={`Delete capability "${deletingTarget?.capabilityName}"?`}
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
