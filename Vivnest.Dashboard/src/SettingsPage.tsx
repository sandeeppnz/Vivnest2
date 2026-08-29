import { useState } from "react";
import { type WhoAmI } from "./api";
import { LogoutIcon, SettingsIcon } from "./icons";
import { verifyPin } from "./mode";

interface SettingsPageProps {
  site: Pick<WhoAmI, "tenantId" | "siteId" | "tenantName" | "siteName"> | null;
  onLogout: () => void;
  // Present only for a full-access key in Home Mode - a devicesOnly key
  // is locked to Home Mode server-side, so it gets no unlock row at all.
  onUnlockInstaller?: () => void;
}

// Home Mode's Settings tab (dashboard-redesign-plan.md D4) - the
// mockup's Settings screen holds site/profile, notifications, account
// and the PIN-gated installer unlock; only the parts that exist today
// are rendered, and the deferred ones are named as coming rather than
// silently absent, same convention as the Sidebar's "soon" rows.
export function SettingsPage({ site, onLogout, onUnlockInstaller }: SettingsPageProps) {
  const [unlocking, setUnlocking] = useState(false);
  const [pin, setPin] = useState("");
  const [pinError, setPinError] = useState<string | null>(null);

  async function tryUnlock() {
    if (await verifyPin(pin)) {
      setPin("");
      onUnlockInstaller?.();
      return;
    }

    setPinError("Wrong PIN.");
    setPin("");
  }

  return (
    <>
      <h3 className="section-heading">Site</h3>
      <div className="metric-grid">
        <div className="metric-cell">
          <div className="metric-cell-label">Tenant</div>
          <div className="metric-cell-value">{site?.tenantName ?? site?.tenantId ?? "—"}</div>
        </div>
        <div className="metric-cell">
          <div className="metric-cell-label">Site</div>
          <div className="metric-cell-value">{site?.siteName ?? site?.siteId ?? "—"}</div>
        </div>
      </div>

      <h3 className="section-heading">Account</h3>
      <div className="entity-list">
        <button type="button" className="entity-row" onClick={onLogout}>
          <div className="entity-row-main">
            <span className="icon-badge">
              <LogoutIcon className="device-icon" />
            </span>
            <div>
              <div className="entity-row-title">Log out</div>
              <div className="entity-row-subtitle">Forget this device key on this browser</div>
            </div>
          </div>
        </button>
      </div>

      {onUnlockInstaller && (
        <>
          <h3 className="section-heading">Installer</h3>
          <div className="entity-list">
            <button type="button" className="entity-row" onClick={() => { setUnlocking((u) => !u); setPinError(null); }}>
              <div className="entity-row-main">
                <span className="icon-badge">
                  <SettingsIcon className="device-icon" />
                </span>
                <div>
                  <div className="entity-row-title">Installer mode</div>
                  <div className="entity-row-subtitle">Agents, deploys and admin - PIN required</div>
                </div>
              </div>
            </button>
          </div>
          {unlocking && (
            <form
              className="list-toolbar"
              onSubmit={(e) => {
                e.preventDefault();
                void tryUnlock();
              }}
            >
              <input
                type="password"
                inputMode="numeric"
                className="form-input"
                value={pin}
                onChange={(e) => setPin(e.target.value)}
                placeholder="PIN"
                autoFocus
                style={{ maxWidth: "10rem" }}
              />
              <button type="submit" className="form-dialog-save">
                Unlock
              </button>
              {pinError && <p className="form-dialog-error" style={{ margin: 0 }}>{pinError}</p>}
            </form>
          )}
        </>
      )}

      <h3 className="section-heading">Coming later</h3>
      <p className="form-hint">
        Notifications live here eventually - deliberately not built yet (see
        docs/roadmap/dashboard-redesign-plan.md).
      </p>
    </>
  );
}
