import type { AgentSummary } from "./api";
import { AgentIcon } from "./icons";

interface AgentRowProps {
  agent: AgentSummary;
  onClick: () => void;
}

// Shared entity-row rendering for an agent - used by AgentList and
// Overview's "Needs attention" list.
export function AgentRow({ agent, onClick }: AgentRowProps) {
  return (
    <button type="button" className="entity-row" onClick={onClick}>
      <div className="entity-row-main">
        <span className={`icon-badge icon-badge-${agent.status.toLowerCase()}`}>
          <AgentIcon className="device-icon" />
        </span>
        <div>
          <div className="entity-row-title">{agent.name || agent.agentId}</div>
          <div className="entity-row-subtitle">
            <span className={`status-dot status-dot-${agent.status.toLowerCase()}`} />
            <span>Agent</span>
            {/* Decision-log.md ADR-077 - only shown when something's
                actually worth a glance (not UpToDate/NeverPublished or
                NeverDeployed) - keeps a normal, healthy row uncluttered,
                same "surface problems, not everything" philosophy
                Overview's own "Needs attention" list already uses. */}
            {agent.configurationStatus.status !== "UpToDate" &&
              agent.configurationStatus.status !== "NeverPublished" && (
                <span
                  className="entity-row-agent"
                  title={`Configuration: ${agent.configurationStatus.status}`}
                >
                  {" · "}
                  <span className={`status-dot status-dot-${agent.configurationStatus.status === "Failed" ? "error" : "warning"}`} />
                  cfg
                </span>
              )}
            {agent.versionStatus.status !== "UpToDate" &&
              agent.versionStatus.status !== "NeverDeployed" && (
                <span
                  className="entity-row-agent"
                  title={`Software: ${agent.versionStatus.status}`}
                >
                  {" · "}
                  <span className="status-dot status-dot-warning" />
                  ver
                </span>
              )}
          </div>
        </div>
      </div>
    </button>
  );
}
