import { useEffect, useState } from "react";
import { ApiError, getAgents, type AgentSummary } from "./api";
import { formatDateTime, formatDateTimeExact, formatInterval } from "./format";
import { AgentIcon, HeartbeatIcon, IntervalIcon } from "./icons";

interface AgentListProps {
  apiKey: string;
  onSelect: (agentId: string) => void;
  onAuthError: () => void;
}

export function AgentList({ apiKey, onSelect, onAuthError }: AgentListProps) {
  const [agents, setAgents] = useState<AgentSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    getAgents(apiKey)
      .then((result) => {
        if (!cancelled) setAgents(result);
      })
      .catch((err) => {
        if (cancelled) return;

        if (err instanceof ApiError && err.status === 401) {
          onAuthError();
          return;
        }

        setError(err instanceof Error ? err.message : "Failed to load agents.");
      });

    return () => {
      cancelled = true;
    };
  }, [apiKey, onAuthError]);

  if (error) return <p className="error">{error}</p>;
  if (!agents) return <p>Loading agents...</p>;
  if (agents.length === 0) return <p>No agents reporting yet.</p>;

  return (
    <div className="entity-list">
      {agents.map((agent) => (
        <button
          type="button"
          key={agent.agentId}
          className="entity-row"
          onClick={() => onSelect(agent.agentId)}
        >
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
          <div className="entity-row-meta">
            <span className="entity-row-interval" title={formatDateTimeExact(agent.lastHeartbeatUtc)}>
              <HeartbeatIcon className="entity-row-interval-icon" />
              {formatDateTime(agent.lastHeartbeatUtc)}
            </span>
            <span className="entity-row-interval">
              <IntervalIcon className="entity-row-interval-icon" />
              {formatInterval(agent.heartbeatInterval)}
            </span>
          </div>
        </button>
      ))}
    </div>
  );
}
