import { useEffect, useState } from "react";
import { ApiError, getDeviceCapabilities, type CapabilityService, type DeviceCapabilities } from "./api";
import { formatInterval } from "./format";
import { DeviceIcon } from "./icons";

interface CapabilitiesTabProps {
  apiKey: string;
  deviceId: string;
  onSelectDevice: (deviceId: string) => void;
  onAuthError: () => void;
}

// Order matches how the backend already builds the list (Built-in first,
// then Derived, then System) - see DeviceCapabilitiesQueryService.BuildCapabilitiesAsync.
const CAPABILITY_GROUPS = ["Built-in", "Derived", "System"] as const;

// A capability (e.g. "Image Classification") is the canonical name shown as
// the group label; each service under it (e.g. "SinkCleanliness") is the
// concrete device/service actually providing it, with its own row and its
// own Enabled state - see decision-log.md ADR-041. Today every capability
// has exactly one service, but this renders however many there are.
function describeService(svc: CapabilityService): string[] {
  const parts: string[] = [];

  if (svc.host) parts.push(svc.host);
  if (svc.username) parts.push(svc.username);

  if (svc.roiLeft !== null && svc.roiTop !== null && svc.roiRight !== null && svc.roiBottom !== null) {
    parts.push(`ROI ${svc.roiLeft},${svc.roiTop}–${svc.roiRight},${svc.roiBottom}`);
  }

  if (svc.modelPath) parts.push(svc.modelPath);
  if (svc.confidenceThreshold !== null) parts.push(`confidence ≥ ${svc.confidenceThreshold}`);

  if (svc.livenessInterval) parts.push(`every ${formatInterval(svc.livenessInterval)}`);
  if (svc.warningMultiplier !== null) parts.push(`warn at ${svc.warningMultiplier}×`);
  if (svc.executingAgentId) parts.push(`agent ${svc.executingAgentId}`);

  return parts;
}

export function CapabilitiesTab({ apiKey, deviceId, onSelectDevice, onAuthError }: CapabilitiesTabProps) {
  const [data, setData] = useState<DeviceCapabilities | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    setData(null);
    setError(null);

    getDeviceCapabilities(apiKey, deviceId)
      .then((result) => !cancelled && setData(result))
      .catch((err) => {
        if (cancelled) return;

        if (err instanceof ApiError && err.status === 401) {
          onAuthError();
          return;
        }

        setError(err instanceof Error ? err.message : "Failed to load capabilities.");
      });

    return () => {
      cancelled = true;
    };
  }, [apiKey, deviceId, onAuthError]);

  if (error) return <p className="error">{error}</p>;
  if (!data) return <p>Loading capabilities...</p>;

  return (
    <>
      <h3 className="section-heading">Capabilities</h3>
      {CAPABILITY_GROUPS.map((group) => {
        const caps = data.capabilities.filter((cap) => cap.source === group);

        if (caps.length === 0) return null;

        return (
          <div key={group}>
            <h4 className="subsection-heading">{group}</h4>
            {caps.map((cap) => (
              <div key={cap.name}>
                <h5 className="capability-heading">{cap.name}</h5>
                <div className="entity-list">
                  {cap.services.map((svc) => (
                    <div className="entity-row entity-row-static" key={svc.name}>
                      <div className="entity-row-main">
                        <div>
                          <div className="entity-row-title">{svc.name}</div>
                          <div className="entity-row-subtitle">{describeService(svc).join(" · ")}</div>
                        </div>
                      </div>
                      <span className={`status ${svc.enabled ? "status-online" : "status-unknown"}`}>
                        {svc.enabled ? "Enabled" : "Disabled"}
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>
        );
      })}

      <h3 className="section-heading">Triggered By</h3>
      {data.triggeredBy.length === 0 ? (
        <p>Not triggered by another device.</p>
      ) : (
        <div className="entity-list">
          {data.triggeredBy.map((t) => (
            <button
              type="button"
              className="entity-row"
              key={t.deviceId}
              onClick={() => onSelectDevice(t.deviceId)}
            >
              <div className="entity-row-main">
                <span className="icon-badge">
                  <DeviceIcon deviceType={t.deviceType} className="device-icon" />
                </span>
                <div>
                  <div className="entity-row-title">{t.deviceName}</div>
                  <div className="entity-row-subtitle">{t.deviceType}</div>
                </div>
              </div>
            </button>
          ))}
        </div>
      )}

      <h3 className="section-heading">Source Sensors</h3>
      {data.sourceSensors.length === 0 ? (
        <p>No sensors configured.</p>
      ) : (
        <div className="entity-list">
          {data.sourceSensors.map((s) => (
            <div className="entity-row entity-row-static" key={s.name}>
              <div className="entity-row-main">
                <div>
                  <div className="entity-row-title">{s.name}</div>
                  <div className="entity-row-subtitle">
                    Used by {s.usedByCount} capabilit{s.usedByCount === 1 ? "y" : "ies"}
                    {s.inaccessibleReason && ` · ${s.inaccessibleReason}`}
                  </div>
                </div>
              </div>
              <span className={`status ${s.accessible ? "status-online" : "status-error"}`}>
                {s.accessible ? "Accessible" : "Inaccessible"}
              </span>
            </div>
          ))}
        </div>
      )}
    </>
  );
}
