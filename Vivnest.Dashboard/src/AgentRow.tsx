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
          </div>
        </div>
      </div>
    </button>
  );
}
