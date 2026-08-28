import { useMemo } from "react";
import { type AgentCommand } from "./api";
import { ErrorState } from "./ErrorState";
import { formatDateTime, formatDateTimeExact } from "./format";
import { useAgentCommands, useCapabilityCatalogue } from "./queries";

interface CommandHistoryProps {
  agentId: string;
  // Present on DeviceDetail (filters to this device's own commands, e.g.
  // ExecuteCapability); absent on AgentDetail (shows every command for
  // the whole Agent). Decision-log.md ADR-083 - client-side filter of
  // the same tenant-scoped /agents/{agentId}/commands list, not a
  // dedicated per-device endpoint.
  deviceId?: string;
}

// Decision-log.md ADR-078's established "reuse the shared palette"
// convention - no new CSS selectors, same status/status-* classes every
// other status badge on this dashboard already uses.
const COMMAND_STATUS_CLASS: Record<string, string> = {
  Succeeded: "status-online",
  Failed: "status-error",
  Dispatched: "status-warning",
  Received: "status-warning",
  Executing: "status-warning",
  Pending: "status-warning",
  Expired: "status-unknown",
  Cancelled: "status-unknown",
};

// capabilityNames maps catalogue capabilityId -> capabilityName.
//
// This used to read `capabilityId === "ImageCapture" ? "Capture now" : ...`,
// which stopped working the moment CommandDispatcher started normalising
// every identity to the catalogue id (ADR-102): nothing is stored as
// "ImageCapture" any more, so history rendered "Execute
// 5217f0ef-f7c6-4d9f-9723-7bf2afad5572" for every capture. A display-only
// break, but a live one, and invisible from the code alone - the literal
// still looked plausible.
//
// Resolving through the catalogue instead of special-casing capture again
// means every capability gets a readable label, including ones added
// later, and leaves no legacy identity in the dashboard at all.
function describeCommand(
  command: AgentCommand,
  capabilityNames: Record<string, string>,
): string {
  switch (command.commandType) {
    case "RestartAgent":
      return "Restart";
    case "RefreshConfiguration":
      return "Refresh configuration";
    case "ApplyConfiguration":
      return "Apply configuration";
    case "ExecuteCapability": {
      if (!command.capabilityId) return "Execute capability";

      // Falls back to the raw id rather than hiding it - an id with no
      // catalogue entry is worth seeing, not smoothing over.
      return `Execute ${capabilityNames[command.capabilityId] ?? command.capabilityId}`;
    }
    default:
      return command.commandType;
  }
}

export function CommandHistory({ agentId, deviceId }: CommandHistoryProps) {
  const commandsQuery = useAgentCommands(agentId);
  // The catalogue is fetched alongside, not instead of, the commands: a
  // catalogue failure must not blank out the history, so labels simply
  // degrade to raw ids while it has no data.
  const catalogueQuery = useCapabilityCatalogue();

  const capabilityNames = useMemo(
    () => Object.fromEntries((catalogueQuery.data ?? []).map((c) => [c.capabilityId, c.capabilityName])),
    [catalogueQuery.data],
  );

  const commands = useMemo(
    () =>
      commandsQuery.data
        ? deviceId
          ? commandsQuery.data.filter((c) => c.targetDeviceId === deviceId)
          : commandsQuery.data
        : null,
    [commandsQuery.data, deviceId],
  );

  return (
    <>
      <h3 className="section-heading">Command history</h3>
      {commandsQuery.isError ? (
        <ErrorState message={commandsQuery.error.message} onRetry={() => commandsQuery.refetch()} />
      ) : !commands ? (
        <p>Loading commands...</p>
      ) : commands.length === 0 ? (
        <p>No commands yet.</p>
      ) : (
        <ul className="event-list">
          {commands.map((command) => (
            <li key={command.commandId}>
              <span className="event-type">
                {describeCommand(command, capabilityNames)} · {command.requestedBy}
              </span>
              <span className={`status ${COMMAND_STATUS_CLASS[command.status] ?? "status-unknown"}`}>
                {command.status}
              </span>
              <span
                className="event-time"
                title={formatDateTimeExact(command.completedUtc ?? command.createdUtc)}
              >
                {formatDateTime(command.completedUtc ?? command.createdUtc)}
              </span>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
