import { useEffect, useState } from "react";
import { ApiError, getAgents, type AgentSummary } from "./api";
import { formatDateTime, formatDateTimeExact, formatInterval } from "./format";
import { AgentIcon } from "./icons";

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
          className={`entity-row accent-${agent.status.toLowerCase()}`}
          onClick={() => onSelect(agent.agentId)}
        >
          <div className="entity-row-main">
            <AgentIcon className="device-icon" />
            <div>
              <div className="entity-row-title">{agent.name || agent.agentId}</div>
              <div className="entity-row-subtitle">
                <span className={`status status-${agent.status.toLowerCase()}`}>
                  {agent.status}
                </span>
              </div>
            </div>
          </div>
          <div className="entity-row-meta">
            <span title={formatDateTimeExact(agent.lastHeartbeatUtc)}>
              {formatDateTime(agent.lastHeartbeatUtc)}
            </span>
            <span>checks in every {formatInterval(agent.heartbeatInterval)}</span>
          </div>
        </button>
      ))}
    </div>
  );
}
