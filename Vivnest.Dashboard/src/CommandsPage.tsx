import { useMemo, useState } from "react";
import { COMMAND_STATUS_CLASS, describeCommand } from "./CommandHistory";
import { ErrorState } from "./ErrorState";
import { formatDateTime, formatDateTimeExact } from "./format";
import { useAgents, useAllAgentCommands, useCapabilityCatalogue } from "./queries";

// Developer Mode's cross-agent command debugger: every agent's command
// history merged newest-first, with the parts the operator views hide -
// error codes, error messages, raw ids - laid out to be read. Riding
// the 30s monitoring refresh means a command you just issued marches
// through Pending -> Dispatched -> Succeeded here without reloading.
export function CommandsPage() {
  const agentsQuery = useAgents();
  const commandsQuery = useAllAgentCommands(agentsQuery.data);
  const catalogueQuery = useCapabilityCatalogue();
  const [statusFilter, setStatusFilter] = useState<string | null>(null);

  const capabilityNames = useMemo(
    () => Object.fromEntries((catalogueQuery.data ?? []).map((c) => [c.capabilityId, c.capabilityName])),
    [catalogueQuery.data],
  );

  const commands = commandsQuery.data ?? null;

  const statusCounts = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const c of commands ?? []) counts[c.status] = (counts[c.status] ?? 0) + 1;
    return counts;
  }, [commands]);

  const filtered = useMemo(
    () => (commands ?? []).filter((c) => !statusFilter || c.status === statusFilter),
    [commands, statusFilter],
  );

  if (agentsQuery.isError) {
    return <ErrorState message={agentsQuery.error.message} onRetry={() => agentsQuery.refetch()} />;
  }
  if (commandsQuery.isError) {
    return <ErrorState message={commandsQuery.error.message} onRetry={() => commandsQuery.refetch()} />;
  }
  if (!commands) return <p>Loading commands...</p>;
  if (commands.length === 0) return <p>No commands recorded yet.</p>;

  return (
    <>
      <div className="filter-chips">
        <button
          type="button"
          className={`filter-chip${statusFilter === null ? " active" : ""}`}
          onClick={() => setStatusFilter(null)}
        >
          All <span className="filter-chip-count">{commands.length}</span>
        </button>
        {Object.entries(statusCounts).map(([status, count]) => (
          <button
            type="button"
            key={status}
            className={`filter-chip${statusFilter === status ? " active" : ""}`}
            onClick={() => setStatusFilter(statusFilter === status ? null : status)}
          >
            {status} <span className="filter-chip-count">{count}</span>
          </button>
        ))}
      </div>

      <div className="entity-list">
        {filtered.map((command) => (
          <div className="entity-row entity-row-static" key={command.commandId}>
            <div className="entity-row-main">
              <div>
                <div className="entity-row-title">
                  {describeCommand(command, capabilityNames)}
                </div>
                <div className="entity-row-subtitle">
                  {command.agentName}
                  {command.targetDeviceId && ` · device ${command.targetDeviceId}`}
                  {" · "}by {command.requestedBy}
                  <span title={formatDateTimeExact(command.createdUtc)}>
                    {" · "}
                    {formatDateTime(command.createdUtc)}
                  </span>
                </div>
                <div className="entity-row-subtitle" style={{ fontFamily: "monospace" }}>
                  {command.commandId}
                </div>
                {command.errorMessage && (
                  <div className="entity-row-subtitle" style={{ color: "var(--text-danger)" }}>
                    {command.errorCode && `[${command.errorCode}] `}
                    {command.errorMessage}
                  </div>
                )}
                {command.result && (
                  <div className="entity-row-subtitle">{command.result}</div>
                )}
              </div>
            </div>
            <span className={`status ${COMMAND_STATUS_CLASS[command.status] ?? "status-unknown"}`}>
              {command.status}
            </span>
          </div>
        ))}
      </div>
    </>
  );
}
