import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { useLocation } from "wouter";
import {
  createAgentRegistryEntry,
  deleteAgentRegistryEntry,
  updateAgentRegistryEntry,
  type AgentRegistry,
  type AgentRegistryStatus,
  type AgentRegistryType,
} from "./api";
import { ErrorState } from "./ErrorState";
import { AgentRegistryFormModal } from "./AgentRegistryFormModal";
import { AgentCapabilitiesModal } from "./AgentCapabilitiesModal";
import { ConfirmDialog } from "./ConfirmDialog";
import { EditIcon, LinkIcon, PuzzleIcon, TrashIcon } from "./icons";
import { useAgentRegistryList, useCapabilityCatalogue } from "./queries";
import { useApiKey } from "./session";

const TYPE_STATUS_CLASS: Record<AgentRegistryType, string> = {
  Low: "status-online",
  High: "status-accent",
};

const AGENT_STATUS_CLASS: Record<AgentRegistryStatus, string> = {
  Active: "status-online",
  Inactive: "status-offline",
};

interface SaveInput {
  name: string;
  description: string;
  status: AgentRegistryStatus;
  firmwareVersion: string;
  type: AgentRegistryType;
  runtimeAgentId: string;
}

// Mirrors CapabilitiesAdmin.tsx exactly - see that file for the reasoning
// behind this shape. Which capabilities an Agent declares is
// AgentCapability's job (decision-log.md ADR-059) - the "Manage
// Capabilities" action opens AgentCapabilitiesModal, scoped to that agent.
export function AgentRegistryAdmin() {
  const apiKey = useApiKey();
  const queryClient = useQueryClient();
  const [, navigate] = useLocation();
  const registryQuery = useAgentRegistryList();
  const catalogueQuery = useCapabilityCatalogue();

  const [search, setSearch] = useState("");
  const [editingTarget, setEditingTarget] = useState<AgentRegistry | "new" | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [deletingTarget, setDeletingTarget] = useState<AgentRegistry | null>(null);
  const [capabilitiesTarget, setCapabilitiesTarget] = useState<AgentRegistry | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const filtered = useMemo(() => {
    const agents = registryQuery.data;
    if (!agents) return [];
    if (!search.trim()) return agents;

    const query = search.trim().toLowerCase();
    return agents.filter((a) => a.name.toLowerCase().includes(query));
  }, [registryQuery.data, search]);

  const invalidateRegistry = () => {
    queryClient.invalidateQueries({ queryKey: ["agent-registry"] });
    queryClient.invalidateQueries({ queryKey: ["agent-installations-overview"] });
  };

  const saveMutation = useMutation({
    mutationFn: async (input: SaveInput) => {
      if (editingTarget === "new") {
        await createAgentRegistryEntry(apiKey, input.name, input.description, input.firmwareVersion, input.type, input.runtimeAgentId);
      } else if (editingTarget) {
        await updateAgentRegistryEntry(
          apiKey,
          editingTarget.agentId,
          input.name,
          input.description,
          input.status,
          input.firmwareVersion,
          input.type,
          input.runtimeAgentId,
        );
      }
    },
    onSuccess: () => {
      setEditingTarget(null);
      invalidateRegistry();
    },
    onError: (err) => setSaveError(err.message),
  });

  const deleteMutation = useMutation({
    mutationFn: (target: AgentRegistry) => deleteAgentRegistryEntry(apiKey, target.agentId),
    onSuccess: invalidateRegistry,
    onError: (err) => setActionError(err.message),
    onSettled: () => setDeletingTarget(null),
  });

  if (registryQuery.isError) {
    return <ErrorState message={registryQuery.error.message} onRetry={() => registryQuery.refetch()} />;
  }
  if (!registryQuery.data) return <p>Loading agents...</p>;

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
                  aria-label={`View configuration for ${a.name}`}
                  onClick={() => navigate(`/admin/agents/${encodeURIComponent(a.agentId)}/config`)}
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
        onSave={(name, description, status, firmwareVersion, type, runtimeAgentId) => {
          setSaveError(null);
          saveMutation.mutate({ name, description, status, firmwareVersion, type, runtimeAgentId });
        }}
        onCancel={() => {
          setSaveError(null);
          setEditingTarget(null);
        }}
      />

      <AgentCapabilitiesModal
        open={capabilitiesTarget !== null}
        agent={capabilitiesTarget}
        capabilities={catalogueQuery.data ?? []}
        onClose={() => setCapabilitiesTarget(null)}
      />

      <ConfirmDialog
        open={deletingTarget !== null}
        message={`Delete agent "${deletingTarget?.name}"?`}
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
