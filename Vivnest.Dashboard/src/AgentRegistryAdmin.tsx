import { useEffect, useMemo, useState } from "react";
import {
  ApiError,
  createAgentRegistryEntry,
  deleteAgentRegistryEntry,
  getAgentRegistry,
  updateAgentRegistryEntry,
  type AgentRegistry,
  type AgentRegistryStatus,
  type AgentRegistryType,
} from "./api";
import { AgentRegistryFormModal } from "./AgentRegistryFormModal";
import { ConfirmDialog } from "./ConfirmDialog";
import { EditIcon, TrashIcon } from "./icons";

interface AgentRegistryAdminProps {
  apiKey: string;
  onAuthError: () => void;
}

const TYPE_STATUS_CLASS: Record<AgentRegistryType, string> = {
  Low: "status-online",
  High: "status-accent",
};

const AGENT_STATUS_CLASS: Record<AgentRegistryStatus, string> = {
  Active: "status-online",
  Inactive: "status-offline",
};

// Mirrors CapabilitiesAdmin.tsx exactly - see that file for the reasoning
// behind this shape (client-side filter, entity-list rows, form modal +
// ConfirmDialog for delete). Which capabilities an Agent declares is
// AgentCapability's job now (decision-log.md ADR-059) - no longer fetched
// or shown here, same reasoning DeviceRegistryAdmin.tsx dropped its own
// capability badges/checklist in ADR-057.
export function AgentRegistryAdmin({ apiKey, onAuthError }: AgentRegistryAdminProps) {
  const [agents, setAgents] = useState<AgentRegistry[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [editingTarget, setEditingTarget] = useState<AgentRegistry | "new" | null>(null);
  const [deletingTarget, setDeletingTarget] = useState<AgentRegistry | null>(null);

  function handleError(err: unknown) {
    if (err instanceof ApiError && err.status === 401) {
      onAuthError();
      return;
    }

    setError(err instanceof Error ? err.message : "Something went wrong.");
  }

  function load() {
    setError(null);

    getAgentRegistry(apiKey)
      .then(setAgents)
      .catch(handleError);
  }

  useEffect(() => {
    let cancelled = false;

    setAgents(null);
    setError(null);

    getAgentRegistry(apiKey)
      .then((result) => !cancelled && setAgents(result))
      .catch((err) => !cancelled && handleError(err));

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiKey]);

  const filtered = useMemo(() => {
    if (!agents) return [];
    if (!search.trim()) return agents;

    const query = search.trim().toLowerCase();
    return agents.filter((a) => a.name.toLowerCase().includes(query));
  }, [agents, search]);

  async function handleSave(
    name: string,
    description: string,
    status: AgentRegistryStatus,
    firmwareVersion: string,
    type: AgentRegistryType,
  ) {
    try {
      if (editingTarget === "new") {
        await createAgentRegistryEntry(apiKey, name, description, firmwareVersion, type);
      } else if (editingTarget) {
        await updateAgentRegistryEntry(
          apiKey,
          editingTarget.agentId,
          name,
          description,
          status,
          firmwareVersion,
          type,
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
      await deleteAgentRegistryEntry(apiKey, deletingTarget.agentId);
      setDeletingTarget(null);
      load();
    } catch (err) {
      setDeletingTarget(null);
      handleError(err);
    }
  }

  if (error) return <p className="error">{error}</p>;
  if (!agents) return <p>Loading agents...</p>;

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
        <p>No agents registered yet.</p>
      ) : (
        <div className="entity-list">
          {filtered.map((a) => (
            <div className="entity-row entity-row-static" key={a.agentId}>
              <div className="entity-row-main">
                <div>
                  <div className="entity-row-title">{a.name}</div>
                  <div className="entity-row-subtitle" style={{ fontFamily: "monospace" }}>
                    {a.agentId}
                    {a.firmwareVersion && ` · v${a.firmwareVersion}`}
                  </div>
                </div>
              </div>
              <div className="entity-row-actions">
                <span className={`status ${AGENT_STATUS_CLASS[a.status]}`}>{a.status}</span>
                <span className={`status ${TYPE_STATUS_CLASS[a.type]}`}>{a.type}</span>
                <button
                  type="button"
                  className="icon-button"
                  aria-label={`Edit ${a.name}`}
                  onClick={() => setEditingTarget(a)}
                >
                  <EditIcon />
                </button>
                <button
                  type="button"
                  className="icon-button icon-button-danger"
                  aria-label={`Delete ${a.name}`}
                  onClick={() => setDeletingTarget(a)}
                >
                  <TrashIcon />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      <AgentRegistryFormModal
        open={editingTarget !== null}
        initial={editingTarget === "new" ? null : editingTarget}
        onSave={handleSave}
        onCancel={() => setEditingTarget(null)}
      />

      <ConfirmDialog
        open={deletingTarget !== null}
        message={`Delete agent "${deletingTarget?.name}"?`}
        confirmLabel="Delete"
        onConfirm={handleDelete}
        onCancel={() => setDeletingTarget(null)}
      />
    </>
  );
}
