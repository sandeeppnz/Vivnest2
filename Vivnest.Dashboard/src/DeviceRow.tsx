import type { AgentSummary, DeviceSummary } from "./api";
import { AgentIcon, DeviceIcon, LocationIcon } from "./icons";

interface DeviceRowProps {
  device: DeviceSummary;
  agent?: AgentSummary | null;
  onClick: () => void;
}

// Shared entity-row rendering for a device - used by DeviceList,
// AgentDetail's "Devices on this agent", and DeviceDetail's "Hub" and
// "Connected devices" sections. `agent` is opt-in per caller: pages already
// scoped to a single agent (or in a devicesOnly key) omit it since it'd be
// redundant/unavailable there.
export function DeviceRow({ device, agent, onClick }: DeviceRowProps) {
  return (
    <button type="button" className="entity-row" onClick={onClick}>
      <div className="entity-row-main">
        <span className={`icon-badge icon-badge-${device.status.toLowerCase()}`}>
          <DeviceIcon deviceType={device.deviceType} className="device-icon" />
        </span>
        <div>
          <div className="entity-row-title">{device.name || device.deviceId}</div>
          <div className="entity-row-subtitle">
            <span className={`status-dot status-dot-${device.status.toLowerCase()}`} />
            <span>{device.deviceType}</span>
            {agent && (
              <span className="entity-row-agent">
                {" · "}
                <AgentIcon className="detail-header-agent-icon" />
                {agent.name || agent.agentId}
              </span>
            )}
            {device.location && (
              <span className="entity-row-agent">
                {" · "}
                <LocationIcon className="detail-header-agent-icon" />
                {device.location}
              </span>
            )}
          </div>
        </div>
      </div>
    </button>
  );
}
