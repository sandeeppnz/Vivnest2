import { useMemo, useState } from "react";
import { ErrorState } from "./ErrorState";
import { AgentRow } from "./AgentRow";
import { useAgents } from "./queries";
import { countByStatus, StatusFilterChips } from "./StatusFilterChips";

interface AgentListProps {
  // Owned by the URL (?status=...) since D1 - see DeviceList.
  statusFilter: string | null;
  onStatusFilterChange: (statusFilter: string | null) => void;
  onSelect: (agentId: string) => void;
}

export function AgentList({ statusFilter, onStatusFilterChange, onSelect }: AgentListProps) {
  const agentsQuery = useAgents();
  const [search, setSearch] = useState("");

  const agents = agentsQuery.data ?? null;

  const statusCounts = useMemo(() => countByStatus(agents), [agents]);

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

  if (agentsQuery.isError) {
    return <ErrorState message={agentsQuery.error.message} onRetry={() => agentsQuery.refetch()} />;
  }
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
        <StatusFilterChips counts={statusCounts} selected={statusFilter} onSelect={onStatusFilterChange} />
      </div>

      {filteredAgents && filteredAgents.length === 0 ? (
        <p>No agents match your search.</p>
      ) : (
        <div className="entity-list">
          {filteredAgents?.map((agent) => (
            <AgentRow key={agent.agentId} agent={agent} onClick={() => onSelect(agent.agentId)} />
          ))}
        </div>
      )}
    </>
  );
}
