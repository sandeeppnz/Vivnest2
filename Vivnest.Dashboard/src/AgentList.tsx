import { useEffect, useMemo, useState } from "react";
import { ApiError, getAgents, type AgentSummary } from "./api";
import { AgentIcon } from "./icons";
import { StatusFilterChips } from "./StatusFilterChips";

interface AgentListProps {
  apiKey: string;
  onSelect: (agentId: string) => void;
  onAuthError: () => void;
}

export function AgentList({ apiKey, onSelect, onAuthError }: AgentListProps) {
  const [agents, setAgents] = useState<AgentSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<string | null>(null);

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

  const statusCounts = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const agent of agents ?? []) {
      counts[agent.status] = (counts[agent.status] ?? 0) + 1;
    }
    return counts;
  }, [agents]);

  const filteredAgents = useMemo(() => {
    if (!agents) return null;

    const query = search.trim().toLowerCase();

    return agents.filter((agent) => {
      if (statusFilter && agent.status !== statusFilter) return false;
      if (!query) return true;

      return (
        agent.name.toLowerCase().includes(query) ||
        agent.agentId.toLowerCase().includes(query)
      );
    });
  }, [agents, search, statusFilter]);

  if (error) return <p className="error">{error}</p>;
  if (!agents) return <p>Loading agents...</p>;
  if (agents.length === 0) return <p>No agents reporting yet.</p>;

  return (
    <>
      <div className="list-toolbar">
        <input
          type="text"
          className="list-search"
          placeholder="Search agents..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <StatusFilterChips counts={statusCounts} selected={statusFilter} onSelect={setStatusFilter} />
      </div>

      {filteredAgents && filteredAgents.length === 0 ? (
        <p>No agents match your search.</p>
      ) : (
        <div className="entity-list">
          {filteredAgents?.map((agent) => (
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
            </button>
          ))}
        </div>
      )}
    </>
  );
}
