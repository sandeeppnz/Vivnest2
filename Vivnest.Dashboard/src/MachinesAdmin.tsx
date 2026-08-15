import { useEffect, useMemo, useState } from "react";
import {
  ApiError,
  createMachine,
  getMachines,
  updateMachine,
  type MachineAdmin,
  type MachineStatus,
} from "./api";
import { MachineFormModal } from "./MachineFormModal";
import { EditIcon } from "./icons";

interface MachinesAdminProps {
  apiKey: string;
  onAuthError: () => void;
}

const STATUS_CLASS: Record<MachineStatus, string> = {
  Active: "status-online",
  Offline: "status-offline",
  Retired: "status-accent",
  Decommissioned: "status-offline",
};

// Mirrors DeviceTypesAdmin.tsx - no delete (MachinesFunction has no
// DELETE route, see decision-log.md ADR-053), Status editable instead.
export function MachinesAdmin({ apiKey, onAuthError }: MachinesAdminProps) {
  const [machines, setMachines] = useState<MachineAdmin[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [editingTarget, setEditingTarget] = useState<MachineAdmin | "new" | null>(null);

  function handleError(err: unknown) {
    if (err instanceof ApiError && err.status === 401) {
      onAuthError();
      return;
    }

    setError(err instanceof Error ? err.message : "Something went wrong.");
  }

  function load() {
    setError(null);

    getMachines(apiKey)
      .then(setMachines)
      .catch(handleError);
  }

  useEffect(() => {
    let cancelled = false;

    setMachines(null);
    setError(null);

    getMachines(apiKey)
      .then((result) => !cancelled && setMachines(result))
      .catch((err) => !cancelled && handleError(err));

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiKey]);

  const filtered = useMemo(() => {
    if (!machines) return [];
    if (!search.trim()) return machines;

    const query = search.trim().toLowerCase();
    return machines.filter((m) => m.name.toLowerCase().includes(query));
  }, [machines, search]);

  async function handleSave(
    name: string,
    hostname: string,
    description: string,
    operatingSystem: string,
    architecture: string,
    status: MachineStatus,
  ) {
    const fields = {
      name,
      hostname: hostname || null,
      description: description || null,
      operatingSystem: operatingSystem || null,
      architecture: architecture || null,
    };

    try {
      if (editingTarget === "new") {
        await createMachine(apiKey, fields);
      } else if (editingTarget) {
        await updateMachine(apiKey, editingTarget.machineId, { ...fields, status });
      }

      setEditingTarget(null);
      load();
    } catch (err) {
      handleError(err);
    }
  }

  if (error) return <p className="error">{error}</p>;
  if (!machines) return <p>Loading machines...</p>;

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
                  onClick={() => setEditingTarget(m)}
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
        onSave={handleSave}
        onCancel={() => setEditingTarget(null)}
      />
    </>
  );
}
