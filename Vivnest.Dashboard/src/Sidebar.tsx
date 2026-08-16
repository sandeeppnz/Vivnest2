import type { ReactElement } from "react";
import type { WhoAmI } from "./api";
import type { View } from "./BottomTabBar";
import {
  AgentIcon,
  DevicesIcon,
  EventsIcon,
  LogoutIcon,
  OverviewIcon,
  VivnestLogo,
  type IconProps,
} from "./icons";

// Which Admin screen is open - declared here (not App.tsx) so both App and
// Sidebar can share it without a circular value import, same pattern as
// BottomTabBar's View type.
export type AdminView =
  | "capabilities"
  | "deviceTypes"
  | "devices"
  | "agents"
  | "machines"
  | "agentInstallations"
  | "apiKeys";

interface SidebarProps {
  view: View;
  adminView: AdminView | null;
  site: Pick<WhoAmI, "tenantId" | "siteId" | "tenantName" | "siteName"> | null;
  onSelectView: (view: View) => void;
  onSelectAdmin: (view: AdminView) => void;
  onLogout: () => void;
}

const MAIN_ITEMS: { view: View; label: string; icon: (props: IconProps) => ReactElement }[] = [
  { view: "overview", label: "Overview", icon: OverviewIcon },
  { view: "devices", label: "Devices", icon: DevicesIcon },
  { view: "agents", label: "Agents", icon: AgentIcon },
  { view: "events", label: "Events", icon: EventsIcon },
];

// Same items, order, disabled entries, and API Keys divider as AdminDrawer -
// the two are alternate presentations of one nav, not different menus.
const ADMIN_ITEMS: { admin: AdminView; label: string }[] = [
  { admin: "capabilities", label: "Capabilities" },
  { admin: "deviceTypes", label: "Device Types" },
  { admin: "devices", label: "Devices" },
  { admin: "agents", label: "Agents" },
  { admin: "machines", label: "Machines" },
  { admin: "agentInstallations", label: "Agent Installations" },
];

// Persistent left nav for desktop widths (>=1024px) - hidden on narrow
// viewports, where BottomTabBar + the hamburger AdminDrawer remain the nav
// (both are always rendered; App.css decides which shows). Carries the
// brand + tenant/site and Log out too, since the top header row is hidden
// alongside it at desktop widths.
export function Sidebar({
  view,
  adminView,
  site,
  onSelectView,
  onSelectAdmin,
  onLogout,
}: SidebarProps) {
  return (
    <aside className="sidebar">
      <div className="sidebar-brand">
        <VivnestLogo className="sidebar-logo" />
        <div>
          <div className="sidebar-title">Vivnest</div>
          {site && (
            <div className="sidebar-site">
              {site.tenantName ?? site.tenantId} / {site.siteName ?? site.siteId}
            </div>
          )}
        </div>
      </div>
      <nav className="sidebar-nav">
        {MAIN_ITEMS.map(({ view: itemView, label, icon: Icon }) => (
          <button
            key={itemView}
            type="button"
            className={`sidebar-item${adminView === null && view === itemView ? " active" : ""}`}
            onClick={() => onSelectView(itemView)}
          >
            <Icon className="sidebar-item-icon" />
            {label}
          </button>
        ))}
        <div className="sidebar-section">Admin</div>
        {ADMIN_ITEMS.map(({ admin, label }) => (
          <button
            key={admin}
            type="button"
            className={`sidebar-item${adminView === admin ? " active" : ""}`}
            onClick={() => onSelectAdmin(admin)}
          >
            {label}
          </button>
        ))}
        <div className="sidebar-item-disabled">
          Services <span>soon</span>
        </div>
        <div className="sidebar-item-disabled">
          Automations <span>soon</span>
        </div>
        <div className="sidebar-divider" />
        <button
          type="button"
          className={`sidebar-item${adminView === "apiKeys" ? " active" : ""}`}
          onClick={() => onSelectAdmin("apiKeys")}
        >
          API Keys
        </button>
      </nav>
      <div className="sidebar-footer">
        <button type="button" className="sidebar-item" onClick={onLogout}>
          <LogoutIcon className="sidebar-item-icon" />
          Log out
        </button>
      </div>
    </aside>
  );
}
