import { useEffect, useState } from "react";
import { ApiError, getAgentCommands, type AgentCommand } from "./api";
import { formatDateTime, formatDateTimeExact } from "./format";

interface CommandHistoryProps {
  apiKey: string;
  agentId: string;
  // Present on DeviceDetail (filters to this device's own commands, e.g.
  // ExecuteCapability); absent on AgentDetail (shows every command for
  // the whole Agent). Decision-log.md ADR-083 - client-side filter of
  // the same tenant-scoped /agents/{agentId}/commands list, not a
  // dedicated per-device endpoint.
  deviceId?: string;
  onAuthError: () => void;
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

function describeCommand(command: AgentCommand): string {
  switch (command.commandType) {
    case "RestartAgent":
      return "Restart";
    case "RefreshConfiguration":
      return "Refresh configuration";
    case "ApplyConfiguration":
      return "Apply configuration";
    case "ExecuteCapability":
      return command.capabilityId === "ImageCapture" ? "Capture now" : `Execute ${command.capabilityId ?? "capability"}`;
    default:
      return command.commandType;
  }
}

export function CommandHistory({ apiKey, agentId, deviceId, onAuthError }: CommandHistoryProps) {
  const [commands, setCommands] = useState<AgentCommand[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    setCommands(null);
    setError(null);

    getAgentCommands(apiKey, agentId)
      .then((result) => {
        if (cancelled) return;

        setCommands(deviceId ? result.filter((c) => c.targetDeviceId === deviceId) : result);
      })
      .catch((err) => {
        if (cancelled) return;

        if (err instanceof ApiError && err.status === 401) {
          onAuthError();
          return;
        }

        setError(err instanceof Error ? err.message : "Failed to load command history.");
      });

    return () => {
      cancelled = true;
    };
  }, [apiKey, agentId, deviceId, onAuthError]);

  return (
    <>
      <h3 className="section-heading">Command history</h3>
      {error ? (
        <p className="error">{error}</p>
      ) : !commands ? (
        <p>Loading commands...</p>
      ) : commands.length === 0 ? (
        <p>No commands yet.</p>
      ) : (
        <ul className="event-list">
          {commands.map((command) => (
            <li key={command.commandId}>
              <span className="event-type">
                {describeCommand(command)} · {command.requestedBy}
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
