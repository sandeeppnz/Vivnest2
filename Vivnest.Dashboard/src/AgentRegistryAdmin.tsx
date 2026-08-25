import { useEffect, useMemo, useState } from "react";
import {
  ApiError,
  createAgentRegistryEntry,
  deleteAgentRegistryEntry,
  getAgentRegistry,
  getCapabilities,
  updateAgentRegistryEntry,
  type AgentRegistry,
  type AgentRegistryStatus,
  type AgentRegistryType,
  type CapabilityAdmin,
} from "./api";
import { ErrorState } from "./ErrorState";
import { AgentRegistryFormModal } from "./AgentRegistryFormModal";
import { AgentCapabilitiesModal } from "./AgentCapabilitiesModal";
import { AgentProjectedConfigModal } from "./AgentProjectedConfigModal";
import { ConfirmDialog } from "./ConfirmDialog";
import { EditIcon, LinkIcon, PuzzleIcon, TrashIcon } from "./icons";

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
// AgentCapability's job now (decision-log.md ADR-059) - the row list
// itself no longer shows capability badges (that flat list is gone), but
// a dedicated "Manage Capabilities" action opens AgentCapabilitiesModal,
// scoped to that one agent - same "primary assignment point is the
// entity's own admin row, not a generic cross-cutting screen" principle
// the spec for this phase asked for.
export function AgentRegistryAdmin({ apiKey, onAuthError }: AgentRegistryAdminProps) {
  const [agents, setAgents] = useState<AgentRegistry[] | null>(null);
  const [capabilities, setCapabilities] = useState<CapabilityAdmin[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [reloadNonce, setReloadNonce] = useState(0);

  function retryLoad() {
    setError(null);
    setReloadNonce((n) => n + 1);
  }
  const [search, setSearch] = useState("");
  const [editingTarget, setEditingTarget] = useState<AgentRegistry | "new" | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [deletingTarget, setDeletingTarget] = useState<AgentRegistry | null>(null);
  const [capabilitiesTarget, setCapabilitiesTarget] = useState<AgentRegistry | null>(null);
  const [projectedConfigTarget, setProjectedConfigTarget] = useState<AgentRegistry | null>(null);

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

    getCapabilities(apiKey)
      .then((result) => !cancelled && setCapabilities(result))
      .catch((err) => !cancelled && handleError(err));

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiKey, reloadNonce]);

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
    runtimeAgentId: string,
  ) {
    setSaveError(null);

    try {
      if (editingTarget === "new") {
        await createAgentRegistryEntry(apiKey, name, description, firmwareVersion, type, runtimeAgentId);
      } else if (editingTarget) {
        await updateAgentRegistryEntry(
          apiKey,
          editingTarget.agentId,
          name,
          description,
          status,
          firmwareVersion,
          type,
          runtimeAgentId,
        );
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
      await deleteAgentRegistryEntry(apiKey, deletingTarget.agentId);
      setDeletingTarget(null);
      load();
    } catch (err) {
      setDeletingTarget(null);
      handleError(err);
    }
  }

  if (error) return <ErrorState message={error} onRetry={retryLoad} />;
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
        <button type="button" className="form-dialog-save" onClick={() => { setSaveError(null); setEditingTarget("new"); }}>
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
                  aria-label={`Manage capabilities for ${a.name}`}
                  onClick={() => setCapabilitiesTarget(a)}
                >
                  <PuzzleIcon />
                </button>
                <button
                  type="button"
                  className="icon-button"
                  aria-label={`View projected config for ${a.name}`}
                  onClick={() => setProjectedConfigTarget(a)}
                >
                  <LinkIcon />
                </button>
                <button
                  type="button"
                  className="icon-button"
                  aria-label={`Edit ${a.name}`}
                  onClick={() => { setSaveError(null); setEditingTarget(a); }}
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
        error={saveError}
        onSave={handleSave}
        onCancel={() => {
          setSaveError(null);
          setEditingTarget(null);
        }}
      />

      <AgentCapabilitiesModal
        open={capabilitiesTarget !== null}
        agent={capabilitiesTarget}
        capabilities={capabilities}
        apiKey={apiKey}
        onAuthError={onAuthError}
        onClose={() => setCapabilitiesTarget(null)}
      />

      <AgentProjectedConfigModal
        open={projectedConfigTarget !== null}
        agent={projectedConfigTarget}
        apiKey={apiKey}
        onAuthError={onAuthError}
        onClose={() => setProjectedConfigTarget(null)}
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
