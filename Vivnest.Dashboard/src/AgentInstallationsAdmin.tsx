import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import {
  installAgent,
  moveAgent,
  uninstallAgent,
  type AgentInstallationCreationResult,
  type AgentInstallationStatus,
  type AgentRegistry,
  type AgentVersionStatus,
} from "./api";
import { ErrorState } from "./ErrorState";
import { InstallAgentModal } from "./InstallAgentModal";
import { ConfirmDialog } from "./ConfirmDialog";
import { CheckIcon, CopyIcon } from "./icons";
import { useInstallationsOverview } from "./queries";
import { useApiKey } from "./session";

// Decision-log.md ADR-071/073 - the full provisioning lifecycle, not just
// Installed/Not installed. Pending/Installing/Installed/Updating are all
// "in progress" (status-warning); Active is the only real "healthy" state;
// Decommissioned reuses the same visual as "not installed."
const INSTALLATION_STATUS_CLASS: Record<AgentInstallationStatus, string> = {
  Pending: "status-unknown",
  Installing: "status-warning",
  Installed: "status-warning",
  Updating: "status-warning",
  Active: "status-online",
  Decommissioned: "status-offline",
};

const VERSION_STATUS_CLASS: Record<AgentVersionStatus, string> = {
  NeverDeployed: "status-unknown",
  Unknown: "status-unknown",
  UpToDate: "status-online",
  Outdated: "status-warning",
};

// Not simple CRUD like every other admin screen - Install/Move/Uninstall
// are lifecycle actions on AgentInstallation (decision-log.md ADR-053),
// so this shows each registered Agent's current Machine (if any) rather
// than a raw list of installation rows. The overview query does the
// one-call-per-agent fan-out; see useInstallationsOverview.
export function AgentInstallationsAdmin() {
  const apiKey = useApiKey();
  const queryClient = useQueryClient();
  const overviewQuery = useInstallationsOverview();

  const [installTarget, setInstallTarget] = useState<{ agent: AgentRegistry; mode: "install" | "move" } | null>(null);
  const [uninstallTarget, setUninstallTarget] = useState<AgentRegistry | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  // Decision-log.md ADR-071 - installToken is only ever present in the
  // Install/Move response itself, never retrievable again afterwards. Held
  // here just long enough for the operator to copy it before dismissing.
  const [revealedToken, setRevealedToken] = useState<AgentInstallationCreationResult | null>(null);
  const [copied, setCopied] = useState(false);

  const machineNameById = useMemo(() => {
    const map = new Map<string, string>();
    for (const m of overviewQuery.data?.machines ?? []) map.set(m.machineId, m.name);
    return map;
  }, [overviewQuery.data]);

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["agent-installations-overview"] });

  const installMutation = useMutation({
    mutationFn: async (input: { machineId: string; containerId: string; imageName: string; imageVersion: string }) => {
      if (!installTarget) throw new Error("No install target.");

      const fields = {
        agentId: installTarget.agent.agentId,
        machineId: input.machineId,
        containerId: input.containerId || null,
        imageName: input.imageName || null,
        imageVersion: input.imageVersion || null,
      };

      return installTarget.mode === "install"
        ? installAgent(apiKey, fields)
        : moveAgent(apiKey, fields);
    },
    onSuccess: (result) => {
      setInstallTarget(null);
      setRevealedToken(result);
      setCopied(false);
      invalidate();
    },
    onError: (err) => setSaveError(err.message),
  });

  const uninstallMutation = useMutation({
    mutationFn: (target: AgentRegistry) => uninstallAgent(apiKey, target.agentId),
    onSuccess: invalidate,
    onError: (err) => setActionError(err.message),
    onSettled: () => setUninstallTarget(null),
  });

  async function handleCopyToken() {
    if (!revealedToken) return;

    try {
      await navigator.clipboard.writeText(revealedToken.installToken);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard API can fail (permissions, insecure context) - the token
      // is still visible in the box for a manual copy.
    }
  }

  if (overviewQuery.isError) {
    return <ErrorState message={overviewQuery.error.message} onRetry={() => overviewQuery.refetch()} />;
  }
  if (!overviewQuery.data) return <p>Loading agent installations...</p>;

  const { agents, installations } = overviewQuery.data;

  return (
    <>
      {actionError && <p className="error">{actionError}</p>}

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
                      {installation ? `On ${machineName}` : "Not installed"}
                    </div>
                    {installation?.containerId && (
                      <div className="entity-row-subtitle" style={{ fontFamily: "monospace" }}>
                        {installation.containerId}
                        {installation.imageName && ` · ${installation.imageName}${installation.imageVersion ? `:${installation.imageVersion}` : ""}`}
                      </div>
                    )}
                    {installation?.versionStatus && (
                      <div className="entity-row-subtitle">
                        <span className={`status ${VERSION_STATUS_CLASS[installation.versionStatus.status]}`}>
                          {installation.versionStatus.status}
                        </span>
                        {installation.versionStatus.status !== "NeverDeployed" && (
                          <>
                            {" "}Desired: {installation.versionStatus.desiredVersion ?? "-"} · Running:{" "}
                            {installation.versionStatus.runningVersion ?? "unknown"}
                          </>
                        )}
                      </div>
                    )}
                  </div>
                </div>
                <div className="entity-row-actions">
                  <span className={`status ${installation ? INSTALLATION_STATUS_CLASS[installation.status] : "status-offline"}`}>
                    {installation ? installation.status : "Not installed"}
                  </span>
                  {installation ? (
                    <>
                      <button
                        type="button"
                        className="confirm-dialog-cancel"
                        onClick={() => { setSaveError(null); setInstallTarget({ agent: a, mode: "move" }); }}
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
                      onClick={() => { setSaveError(null); setInstallTarget({ agent: a, mode: "install" }); }}
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
        machines={overviewQuery.data.machines}
        error={saveError}
        onSave={(machineId, containerId, imageName, imageVersion) => {
          setSaveError(null);
          installMutation.mutate({ machineId, containerId, imageName, imageVersion });
        }}
        onCancel={() => {
          setSaveError(null);
          setInstallTarget(null);
        }}
      />

      <ConfirmDialog
        open={uninstallTarget !== null}
        message={`Uninstall "${uninstallTarget?.name}" from its current machine?`}
        confirmLabel="Uninstall"
        onConfirm={() => {
          setActionError(null);
          if (uninstallTarget) uninstallMutation.mutate(uninstallTarget);
        }}
        onCancel={() => setUninstallTarget(null)}
      />

      {revealedToken && (
        <div className="confirm-overlay" onClick={() => setRevealedToken(null)}>
          <div
            className="form-dialog"
            role="dialog"
            aria-modal="true"
            aria-label="Install token"
            onClick={(event) => event.stopPropagation()}
          >
            <p className="form-hint">
              Save this install token now - it will never be shown again. Pass it to{" "}
              <code>Vivnest.Agent.Updater --installtoken</code> on the target machine before it expires
              ({new Date(revealedToken.installTokenExpiresUtc).toLocaleString()}).
            </p>
            <div className="list-toolbar">
              <code style={{ userSelect: "all", wordBreak: "break-all" }}>{revealedToken.installToken}</code>
              <button type="button" className="icon-button" aria-label="Copy install token" onClick={handleCopyToken}>
                {copied ? <CheckIcon /> : <CopyIcon />}
              </button>
            </div>
            <button type="button" className="form-dialog-save" onClick={() => setRevealedToken(null)}>
              Done
            </button>
          </div>
        </div>
      )}
    </>
  );
}
