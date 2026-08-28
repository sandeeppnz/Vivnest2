import { useMemo, useState } from "react";
import { ErrorState } from "./ErrorState";
import { DeviceRow } from "./DeviceRow";
import { useAgents, useDevices } from "./queries";
import { countByStatus, StatusFilterChips } from "./StatusFilterChips";

interface DeviceListProps {
  devicesOnly: boolean;
  // Owned by the URL (?status=...) since D1, so a filtered list is a
  // real, shareable link - App maps it to navigation.
  statusFilter: string | null;
  onStatusFilterChange: (statusFilter: string | null) => void;
  onSelect: (deviceId: string) => void;
}

export function DeviceList({
  devicesOnly,
  statusFilter,
  onStatusFilterChange,
  onSelect,
}: DeviceListProps) {
  const devicesQuery = useDevices();
  // DevicesOnly keys get 403 from /agents - skip the query entirely
  // rather than fetch-then-fail, same as before the query layer.
  const agentsQuery = useAgents(!devicesOnly);
  const [search, setSearch] = useState("");

  const devices = devicesQuery.data ?? null;
  const agents = agentsQuery.data ?? null;

  const statusCounts = useMemo(() => countByStatus(devices), [devices]);

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

  if (devicesQuery.isError) {
    return <ErrorState message={devicesQuery.error.message} onRetry={() => devicesQuery.refetch()} />;
  }
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
        <StatusFilterChips counts={statusCounts} selected={statusFilter} onSelect={onStatusFilterChange} />
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
