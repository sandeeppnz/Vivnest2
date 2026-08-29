import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { useLocation } from "wouter";
import {
  createDeviceRegistryEntry,
  updateDeviceRegistryEntry,
  type DeviceRegistry,
  type DeviceRegistryFields,
  type DeviceRegistryStatus,
} from "./api";
import { ErrorState } from "./ErrorState";
import { DeviceRegistryFormModal } from "./DeviceRegistryFormModal";
import { DeviceCapabilitiesModal } from "./DeviceCapabilitiesModal";
import { EditIcon, LinkIcon, PuzzleIcon } from "./icons";
import {
  useAgentRegistryList,
  useCapabilityCatalogue,
  useDeviceRegistryList,
  useDeviceTypeCatalogue,
} from "./queries";
import { useApiKey } from "./session";

const STATUS_CLASS: Record<DeviceRegistryStatus, string> = {
  Active: "status-online",
  Disabled: "status-offline",
  Retired: "status-accent",
};

// Mirrors AgentRegistryAdmin.tsx's shape (decision-log.md ADR-048) - the
// device-type and agent registries ride the shared query cache purely for
// client-side cross-referencing (id -> name). No Delete action (ADR-058) -
// same "no DELETE route, retire via Status instead" reasoning as
// MachinesAdmin.tsx.
export function DeviceRegistryAdmin() {
  const apiKey = useApiKey();
  const queryClient = useQueryClient();
  const [, navigate] = useLocation();
  const devicesQuery = useDeviceRegistryList();
  const deviceTypesQuery = useDeviceTypeCatalogue();
  const agentsQuery = useAgentRegistryList();
  const catalogueQuery = useCapabilityCatalogue();

  const [search, setSearch] = useState("");
  const [editingTarget, setEditingTarget] = useState<DeviceRegistry | "new" | null>(null);
  const [capabilitiesTarget, setCapabilitiesTarget] = useState<DeviceRegistry | null>(null);
  // A save failure (e.g. the Settings credential guard rejecting a key)
  // shows inline in the still-open modal - see the form modal's comment.
  const [saveError, setSaveError] = useState<string | null>(null);

  const deviceTypes = useMemo(() => deviceTypesQuery.data ?? [], [deviceTypesQuery.data]);
  const agents = useMemo(() => agentsQuery.data ?? [], [agentsQuery.data]);

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
    const devices = devicesQuery.data;
    if (!devices) return [];
    if (!search.trim()) return devices;

    const query = search.trim().toLowerCase();
    return devices.filter((d) => d.name.toLowerCase().includes(query));
  }, [devicesQuery.data, search]);

  const saveMutation = useMutation({
    mutationFn: async (input: { fields: DeviceRegistryFields; status: DeviceRegistryStatus }) => {
      if (editingTarget === "new") {
        await createDeviceRegistryEntry(apiKey, input.fields);
      } else if (editingTarget) {
        await updateDeviceRegistryEntry(apiKey, editingTarget.deviceId, input.fields, input.status);
      }
    },
    onSuccess: () => {
      setEditingTarget(null);
      queryClient.invalidateQueries({ queryKey: ["device-registry"] });
    },
    onError: (err) => setSaveError(err.message),
  });

  if (devicesQuery.isError) {
    return <ErrorState message={devicesQuery.error.message} onRetry={() => devicesQuery.refetch()} />;
  }
  if (!devicesQuery.data) return <p>Loading devices...</p>;

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
          onClick={() => navigate("/admin/devices/new")}
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
                <span className={`status ${STATUS_CLASS[d.status]}`}>{d.status}</span>
                <button
                  type="button"
                  className="icon-button"
                  aria-label={`Manage capabilities for ${d.name}`}
                  onClick={() => setCapabilitiesTarget(d)}
                >
                  <PuzzleIcon />
                </button>
                <button
                  type="button"
                  className="icon-button"
                  aria-label={`View configuration for ${d.name}`}
                  onClick={() => navigate(`/admin/devices/${encodeURIComponent(d.deviceId)}/config`)}
                >
                  <LinkIcon />
                </button>
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
        onSave={(fields, status) => {
          setSaveError(null);
          saveMutation.mutate({ fields, status });
        }}
        onCancel={() => {
          setSaveError(null);
          setEditingTarget(null);
        }}
      />

      <DeviceCapabilitiesModal
        open={capabilitiesTarget !== null}
        device={capabilitiesTarget}
        capabilities={catalogueQuery.data ?? []}
        agents={agents}
        onClose={() => setCapabilitiesTarget(null)}
      />

    </>
  );
}
