import { useEffect, useState } from "react";
import {
  ApiError,
  getProjectedDeviceConfig,
  type DeviceRegistry,
  type ProjectedDeviceConfig,
} from "./api";

interface ProjectedConfigModalProps {
  open: boolean;
  device: DeviceRegistry | null;
  apiKey: string;
  onAuthError: () => void;
  onClose: () => void;
}

// Read-only preview of what the Admin domain would project as this
// Device's runtime device-config/*.json shape (decision-log.md ADR-063) -
// nothing here writes anywhere. An admin diffs this against the real
// device-config/{runtimeDeviceId}.json file by eye. Warnings surface each
// unresolved link (RuntimeDeviceId not set, OwningAgentId's RuntimeAgentId
// not set, unmatched DeviceType) instead of silently producing a
// misleading preview.
export function ProjectedConfigModal({ open, device, apiKey, onAuthError, onClose }: ProjectedConfigModalProps) {
  const [projected, setProjected] = useState<ProjectedDeviceConfig | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open || !device) return;

    setProjected(null);
    setError(null);

    getProjectedDeviceConfig(apiKey, device.deviceId)
      .then(setProjected)
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) {
          onAuthError();
          return;
        }

        setError(err instanceof Error ? err.message : "Something went wrong.");
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, device]);

  useEffect(() => {
    if (!open) return;

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        onClose();
      }
    }

    window.addEventListener("keydown", handleKeyDown);

    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [open, onClose]);

  if (!open || !device) return null;

  return (
    <div className="confirm-overlay" onClick={onClose}>
      <div
        className="form-dialog"
        role="dialog"
        aria-modal="true"
        aria-label={`${device.name} - Projected Config`}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="form-field">
          <label className="form-label">{device.name} - Projected Config</label>
          <p className="form-hint">
            Preview only - nothing here writes to any real device-config file. Compare against the real
            file by eye.
          </p>
        </div>

        {error && <p className="form-dialog-error">{error}</p>}

        {!error && !projected && <p>Loading projected config...</p>}

        {projected && (
          <>
            {projected.warnings.length > 0 && (
              <div className="form-field">
                <label className="form-label">Warnings</label>
                {projected.warnings.map((warning, index) => (
                  <p className="form-dialog-error" key={index}>
                    {warning}
                  </p>
                ))}
              </div>
            )}
            <div className="form-field">
              <pre className="form-json-preview">
                {JSON.stringify(
                  {
                    DeviceId: projected.deviceId,
                    Name: projected.name,
                    Type: projected.type,
                    Enabled: projected.enabled,
                    Location: projected.location,
                    Brand: projected.brand,
                    Model: projected.model,
                    Firmware: projected.firmware,
                    OwningAgentId: projected.owningAgentId,
                    Settings: projected.settings,
                  },
                  null,
                  2,
                )}
              </pre>
            </div>
          </>
        )}

        <div className="confirm-dialog-actions">
          <button type="button" className="confirm-dialog-cancel" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
