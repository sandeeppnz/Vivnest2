import { type WhoAmI } from "./api";
import { LogoutIcon } from "./icons";

interface SettingsPageProps {
  site: Pick<WhoAmI, "tenantId" | "siteId" | "tenantName" | "siteName"> | null;
  onLogout: () => void;
}

// Home Mode's Settings tab (dashboard-redesign-plan.md D4) - the
// mockup's Settings screen holds site/profile, notifications, account
// and the PIN-gated installer unlock; only the parts that exist today
// are rendered, and the deferred ones are named as coming rather than
// silently absent, same convention as the Sidebar's "soon" rows.
export function SettingsPage({ site, onLogout }: SettingsPageProps) {
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

      <h3 className="section-heading">Coming later</h3>
      <p className="form-hint">
        Notifications and the installer-mode unlock live here eventually - deliberately not built
        yet (see docs/roadmap/dashboard-redesign-plan.md).
      </p>
    </>
  );
}
