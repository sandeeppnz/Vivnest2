import { useEffect, useState } from "react";
import { ApiError, getAgents, type AgentSummary } from "./api";
import { formatInterval } from "./format";

interface AgentListProps {
  apiKey: string;
  onAuthError: () => void;
}

export function AgentList({ apiKey, onAuthError }: AgentListProps) {
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
    <table className="device-table">
      <thead>
        <tr>
          <th>Agent</th>
          <th>Host</th>
          <th>Status</th>
          <th>Status since</th>
          <th>Started</th>
          <th>Last heartbeat</th>
          <th>Heartbeat interval</th>
        </tr>
      </thead>
      <tbody>
        {agents.map((agent) => (
          <tr key={agent.agentId}>
            <td>{agent.agentId}</td>
            <td>{agent.hostName}</td>
            <td>
              <span className={`status status-${agent.status.toLowerCase()}`}>
                {agent.status}
              </span>
            </td>
            <td>{new Date(agent.statusSinceUtc).toLocaleString()}</td>
            <td>{new Date(agent.startedUtc).toLocaleString()}</td>
            <td>{new Date(agent.lastHeartbeatUtc).toLocaleString()}</td>
            <td>{formatInterval(agent.heartbeatInterval)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
