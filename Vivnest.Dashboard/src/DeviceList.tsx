import { useEffect, useMemo, useState } from "react";
import { ApiError, getAgents, getDevices, type AgentSummary, type DeviceSummary } from "./api";
import { DeviceRow } from "./DeviceRow";
import { StatusFilterChips } from "./StatusFilterChips";

interface DeviceListProps {
  apiKey: string;
  devicesOnly: boolean;
  onSelect: (deviceId: string) => void;
  onAuthError: () => void;
}

export function DeviceList({ apiKey, devicesOnly, onSelect, onAuthError }: DeviceListProps) {
  const [devices, setDevices] = useState<DeviceSummary[] | null>(null);
  const [agents, setAgents] = useState<AgentSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    function handleError(err: unknown) {
      if (cancelled) return;

      if (err instanceof ApiError && err.status === 401) {
        onAuthError();
        return;
      }

      setError(err instanceof Error ? err.message : "Failed to load devices.");
    }

    getDevices(apiKey)
      .then((result) => !cancelled && setDevices(result))
      .catch(handleError);

    // DevicesOnly keys get 403 from /agents - skip the call entirely
    // rather than fetch-then-fail, same as DeviceDetail's header.
    if (!devicesOnly) {
      getAgents(apiKey)
        .then((result) => !cancelled && setAgents(result))
        .catch(handleError);
    }

    return () => {
      cancelled = true;
    };
  }, [apiKey, devicesOnly, onAuthError]);

  const statusCounts = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const device of devices ?? []) {
      counts[device.status] = (counts[device.status] ?? 0) + 1;
    }
    return counts;
  }, [devices]);

  const filteredDevices = useMemo(() => {
    if (!devices) return null;

    const query = search.trim().toLowerCase();

    return devices.filter((device) => {
      if (statusFilter && device.status !== statusFilter) return false;
      if (!query) return true;

      return (
        device.name.toLowerCase().includes(query) ||
        device.deviceId.toLowerCase().includes(query) ||
        device.deviceType.toLowerCase().includes(query) ||
        device.location.toLowerCase().includes(query)
      );
    });
  }, [devices, search, statusFilter]);

  if (error) return <p className="error">{error}</p>;
  if (!devices) return <p>Loading devices...</p>;
  if (devices.length === 0) return <p>No devices reporting yet.</p>;

  return (
    <>
      <div className="list-toolbar">
        <input
          type="text"
          className="list-search"
          placeholder="Search devices..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <StatusFilterChips counts={statusCounts} selected={statusFilter} onSelect={setStatusFilter} />
      </div>

      {filteredDevices && filteredDevices.length === 0 ? (
        <p>No devices match your search.</p>
      ) : (
        <div className="entity-list">
          {filteredDevices?.map((device) => {
            const agent = agents?.find((a) => a.agentId === device.agentId) ?? null;

            return (
              <DeviceRow
                key={device.deviceId}
                device={device}
                agent={agent}
                onClick={() => onSelect(device.deviceId)}
              />
            );
          })}
        </div>
      )}
    </>
  );
}
