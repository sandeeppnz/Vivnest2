import { useEffect, useState } from "react";
import { ApiError, getDeviceBattery, type DeviceEvent } from "./api";
import { formatDateTime, formatDateTimeExact } from "./format";

interface BatteryStatusProps {
  apiKey: string;
  deviceId: string;
  onAuthError: () => void;
}

// The T100 only ever reports a low-battery boolean (at_low_battery), no
// numeric percentage - confirmed against the real device, not assumed.
function isBatteryLow(event: DeviceEvent): boolean | null {
  const data = event.data;

  if (typeof data !== "object" || data === null) return null;

  const value = (data as { BatteryLow?: unknown }).BatteryLow;

  return typeof value === "boolean" ? value : null;
}

// Tapo's local API reports this as a small ordinal (bars), not dBm - real
// T100 readings observed so far are all "1". No confirmed max value yet,
// so this shows the raw number rather than inventing a Weak/Good label
// scale that might not match the device's actual range.
function getSignalLevel(event: DeviceEvent): number | null {
  const data = event.data;

  if (typeof data !== "object" || data === null) return null;

  const value = (data as { SignalLevel?: unknown }).SignalLevel;

  return typeof value === "number" ? value : null;
}

export function BatteryStatus({ apiKey, deviceId, onAuthError }: BatteryStatusProps) {
  const [readings, setReadings] = useState<DeviceEvent[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    setReadings(null);
    setError(null);

    getDeviceBattery(apiKey, deviceId, 1)
      .then((result) => {
        if (!cancelled) setReadings(result);
      })
      .catch((err) => {
        if (cancelled) return;

        if (err instanceof ApiError && err.status === 401) {
          onAuthError();
          return;
        }

        setError(err instanceof Error ? err.message : "Failed to load battery status.");
      });

    return () => {
      cancelled = true;
    };
  }, [apiKey, deviceId, onAuthError]);

  if (error) return <p className="error">{error}</p>;
  if (!readings || readings.length === 0) return null;

  const latest = readings[0];
  const latestLow = isBatteryLow(latest);
  const signalLevel = getSignalLevel(latest);

  return (
    <>
      <h3 className="section-heading">Battery</h3>
      <div className="metric-grid">
        <div className="metric-cell">
          <div className="metric-cell-label">Status</div>
          <div
            className={
              latestLow === null
                ? "metric-cell-value"
                : `metric-cell-value ${latestLow ? "battery-low" : "battery-ok"}`
            }
          >
            {latestLow === null ? "Unknown" : latestLow ? "Low" : "OK"}
          </div>
        </div>
        <div className="metric-cell">
          <div className="metric-cell-label">Signal</div>
          <div className="metric-cell-value">
            {signalLevel === null ? "Unknown" : signalLevel}
          </div>
        </div>
        <div className="metric-cell">
          <div className="metric-cell-label">Last checked</div>
          <div className="metric-cell-value" title={formatDateTimeExact(latest.occurredAtUtc)}>
            {formatDateTime(latest.occurredAtUtc)}
          </div>
        </div>
      </div>
    </>
  );
}
