import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { createMachine, updateMachine, type MachineAdmin, type MachineStatus } from "./api";
import { ErrorState } from "./ErrorState";
import { MachineFormModal } from "./MachineFormModal";
import { EditIcon } from "./icons";
import { useMachines } from "./queries";
import { useApiKey } from "./session";

const STATUS_CLASS: Record<MachineStatus, string> = {
  Active: "status-online",
  Offline: "status-offline",
  Retired: "status-accent",
  Decommissioned: "status-offline",
};

interface SaveInput {
  name: string;
  hostname: string;
  description: string;
  operatingSystem: string;
  architecture: string;
  status: MachineStatus;
}

// Mirrors DeviceTypesAdmin.tsx - no delete (MachinesFunction has no
// DELETE route, see decision-log.md ADR-053), Status editable instead.
export function MachinesAdmin() {
  const apiKey = useApiKey();
  const queryClient = useQueryClient();
  const machinesQuery = useMachines();

  const [search, setSearch] = useState("");
  const [editingTarget, setEditingTarget] = useState<MachineAdmin | "new" | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  const filtered = useMemo(() => {
    const machines = machinesQuery.data;
    if (!machines) return [];
    if (!search.trim()) return machines;

    const query = search.trim().toLowerCase();
    return machines.filter((m) => m.name.toLowerCase().includes(query));
  }, [machinesQuery.data, search]);

  const saveMutation = useMutation({
    mutationFn: async (input: SaveInput) => {
      const fields = {
        name: input.name,
        hostname: input.hostname || null,
        description: input.description || null,
        operatingSystem: input.operatingSystem || null,
        architecture: input.architecture || null,
      };

      if (editingTarget === "new") {
        await createMachine(apiKey, fields);
      } else if (editingTarget) {
        await updateMachine(apiKey, editingTarget.machineId, { ...fields, status: input.status });
      }
    },
    onSuccess: () => {
      setEditingTarget(null);
      queryClient.invalidateQueries({ queryKey: ["machines"] });
      // The installations overview joins machine names in.
      queryClient.invalidateQueries({ queryKey: ["agent-installations-overview"] });
    },
    onError: (err) => setSaveError(err.message),
  });

  if (machinesQuery.isError) {
    return <ErrorState message={machinesQuery.error.message} onRetry={() => machinesQuery.refetch()} />;
  }
  if (!machinesQuery.data) return <p>Loading machines...</p>;

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
        <p>No machines registered yet.</p>
      ) : (
        <div className="entity-list">
          {filtered.map((m) => (
            <div className="entity-row entity-row-static" key={m.machineId}>
              <div className="entity-row-main">
                <div>
                  <div className="entity-row-title">{m.name}</div>
                  <div className="entity-row-subtitle" style={{ fontFamily: "monospace" }}>
                    {m.machineId}
                  </div>
                  {(m.hostname || m.operatingSystem || m.architecture) && (
                    <div className="entity-row-subtitle">
                      {[m.hostname, m.operatingSystem, m.architecture].filter(Boolean).join(" · ")}
                    </div>
                  )}
                </div>
              </div>
              <div className="entity-row-actions">
                {/* Decision-log.md ADR-076 - operationalStatus (derived
                    live from installed Agents' health) shown alongside
                    status (the hand-set Admin lifecycle field), never
                    merged into one badge. Reuses the same status-* CSS
                    vocabulary DeviceHeartbeatStatus already has a class
                    for, so no separate class map is needed here. */}
                <span className={`status status-${m.operationalStatus.toLowerCase()}`}>
                  {m.operationalStatus}
                </span>
                <span className={`status ${STATUS_CLASS[m.status]}`}>{m.status}</span>
                <button
                  type="button"
                  className="icon-button"
                  aria-label={`Edit ${m.name}`}
                  onClick={() => { setSaveError(null); setEditingTarget(m); }}
                >
                  <EditIcon />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      <MachineFormModal
        open={editingTarget !== null}
        initial={editingTarget === "new" ? null : editingTarget}
        error={saveError}
        onSave={(name, hostname, description, operatingSystem, architecture, status) => {
          setSaveError(null);
          saveMutation.mutate({ name, hostname, description, operatingSystem, architecture, status });
        }}
        onCancel={() => {
          setSaveError(null);
          setEditingTarget(null);
        }}
      />
    </>
  );
}
