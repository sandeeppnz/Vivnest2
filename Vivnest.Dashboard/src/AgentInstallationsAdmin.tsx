import { useEffect, useMemo, useState } from "react";
import {
  ApiError,
  getActiveInstallationByAgent,
  getAgentRegistry,
  getMachines,
  installAgent,
  moveAgent,
  uninstallAgent,
  type AgentInstallation,
  type AgentRegistry,
  type MachineAdmin,
} from "./api";
import { InstallAgentModal } from "./InstallAgentModal";
import { ConfirmDialog } from "./ConfirmDialog";

interface AgentInstallationsAdminProps {
  apiKey: string;
  onAuthError: () => void;
}

// Not simple CRUD like every other admin screen - Install/Move/Uninstall
// are lifecycle actions on AgentInstallation (decision-log.md ADR-053),
// so this shows each registered Agent's current Machine (if any) rather
// than a raw list of installation rows. "Active installation per agent"
// has no bulk endpoint - fetched one call per Agent via Promise.all,
// fine at this scale (a handful of agents), same reasoning
// DeviceCapabilitiesQueryService's own O(N) scan already uses.
export function AgentInstallationsAdmin({ apiKey, onAuthError }: AgentInstallationsAdminProps) {
  const [agents, setAgents] = useState<AgentRegistry[] | null>(null);
  const [machines, setMachines] = useState<MachineAdmin[]>([]);
  const [installations, setInstallations] = useState<Record<string, AgentInstallation | null>>({});
  const [error, setError] = useState<string | null>(null);
  const [installTarget, setInstallTarget] = useState<{ agent: AgentRegistry; mode: "install" | "move" } | null>(null);
  const [uninstallTarget, setUninstallTarget] = useState<AgentRegistry | null>(null);

  function handleError(err: unknown) {
    if (err instanceof ApiError && err.status === 401) {
      onAuthError();
      return;
    }

    setError(err instanceof Error ? err.message : "Something went wrong.");
  }

  async function load(cancelledRef?: { current: boolean }) {
    setError(null);

    try {
      const [agentList, machineList] = await Promise.all([getAgentRegistry(apiKey), getMachines(apiKey)]);
      if (cancelledRef?.current) return;
      setAgents(agentList);
      setMachines(machineList);

      const entries = await Promise.all(
        agentList.map(async (a) => [a.agentId, await getActiveInstallationByAgent(apiKey, a.agentId)] as const),
      );
      if (cancelledRef?.current) return;
      setInstallations(Object.fromEntries(entries));
    } catch (err) {
      if (!cancelledRef?.current) handleError(err);
    }
  }

  useEffect(() => {
    const cancelledRef = { current: false };

    setAgents(null);
    setError(null);
    load(cancelledRef);

    return () => {
      cancelledRef.current = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiKey]);

  const machineNameById = useMemo(() => {
    const map = new Map<string, string>();
    for (const m of machines) map.set(m.machineId, m.name);
    return map;
  }, [machines]);

  async function handleInstallSave(machineId: string, containerId: string, imageName: string, imageVersion: string) {
    if (!installTarget) return;

    const fields = {
      agentId: installTarget.agent.agentId,
      machineId,
      containerId: containerId || null,
      imageName: imageName || null,
      imageVersion: imageVersion || null,
    };

    try {
      if (installTarget.mode === "install") {
        await installAgent(apiKey, fields);
      } else {
        await moveAgent(apiKey, fields);
      }

      setInstallTarget(null);
      load();
    } catch (err) {
      handleError(err);
    }
  }

  async function handleUninstall() {
    if (!uninstallTarget) return;

    try {
      await uninstallAgent(apiKey, uninstallTarget.agentId);
      setUninstallTarget(null);
      load();
    } catch (err) {
      setUninstallTarget(null);
      handleError(err);
    }
  }

  if (error) return <p className="error">{error}</p>;
  if (!agents) return <p>Loading agent installations...</p>;

  return (
    <>
      {agents.length === 0 ? (
        <p>No agents registered yet - add one under Admin &rarr; Agents first.</p>
      ) : (
        <div className="entity-list">
          {agents.map((a) => {
            const installation = installations[a.agentId];
            const machineName = installation ? machineNameById.get(installation.machineId) ?? installation.machineId : null;

            return (
              <div className="entity-row entity-row-static" key={a.agentId}>
                <div className="entity-row-main">
                  <div>
                    <div className="entity-row-title">{a.name}</div>
                    <div className="entity-row-subtitle">
                      {installation ? `Installed on ${machineName}` : "Not installed"}
                    </div>
                    {installation?.containerId && (
                      <div className="entity-row-subtitle" style={{ fontFamily: "monospace" }}>
                        {installation.containerId}
                        {installation.imageName && ` · ${installation.imageName}${installation.imageVersion ? `:${installation.imageVersion}` : ""}`}
                      </div>
                    )}
                  </div>
                </div>
                <div className="entity-row-actions">
                  <span className={`status ${installation ? "status-online" : "status-offline"}`}>
                    {installation ? "Installed" : "Not installed"}
                  </span>
                  {installation ? (
                    <>
                      <button
                        type="button"
                        className="confirm-dialog-cancel"
                        onClick={() => setInstallTarget({ agent: a, mode: "move" })}
                      >
                        Move
                      </button>
                      <button
                        type="button"
                        className="confirm-dialog-cancel"
                        onClick={() => setUninstallTarget(a)}
                      >
                        Uninstall
                      </button>
                    </>
                  ) : (
                    <button
                      type="button"
                      className="form-dialog-save"
                      onClick={() => setInstallTarget({ agent: a, mode: "install" })}
                    >
                      Install
                    </button>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}

      <InstallAgentModal
        open={installTarget !== null}
        mode={installTarget?.mode ?? "install"}
        agent={installTarget?.agent ?? null}
        machines={machines}
        onSave={handleInstallSave}
        onCancel={() => setInstallTarget(null)}
      />

      <ConfirmDialog
        open={uninstallTarget !== null}
        message={`Uninstall "${uninstallTarget?.name}" from its current machine?`}
        confirmLabel="Uninstall"
        onConfirm={handleUninstall}
        onCancel={() => setUninstallTarget(null)}
      />
    </>
  );
}
