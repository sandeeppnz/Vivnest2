import type { ReactElement } from "react";
import { useLocation } from "wouter";
import {
  AgentIcon,
  BellIcon,
  DevicesIcon,
  EventsIcon,
  HistoryIcon,
  HomeIcon,
  OverviewIcon,
  SettingsIcon,
  VivnestLogo,
  type IconProps,
} from "./icons";
import { NotificationBell } from "./NotificationBell";
import { ThemeToggle } from "./ThemeToggle";

// ONE navigation model for every mode: each mode is a list of
// destinations, rendered as a sidebar at desktop widths and a bottom
// tab bar on narrow ones - the same items in the same order, so muscle
// memory transfers between devices. This replaced the old trio
// (Sidebar + BottomTabBar + HomeBottomTabBar) and the mobile-only
// hamburger/AdminDrawer, which made the full mode run two navigation
// systems at once on a phone. Admin now lives where the 2026-08-07
// mockup put it: inside Settings, on every width - one place, not two.

export interface NavItem {
  path: string;
  label: string;
  icon: (props: IconProps) => ReactElement;
}

// The full-access nav, shared by BOTH User and Developer Modes since
// 2026-08-29: the modes differ only in Settings (Admin + Debug are
// Developer's) and in the admin/debug routes redirecting home for
// User - never in the nav.
export const FULL_NAV: NavItem[] = [
  { path: "/", label: "Overview", icon: OverviewIcon },
  { path: "/agents", label: "Agents", icon: AgentIcon },
  { path: "/devices", label: "Devices", icon: DevicesIcon },
  { path: "/events", label: "Events", icon: EventsIcon },
  { path: "/settings", label: "Settings", icon: SettingsIcon },
];

// The devicesOnly-key nav (the mockup's Home Mode bar): belongs to the
// KEY, not to a chosen mode - a devicesOnly key gets 403 from every
// /agents* route, so this is what the server-enforced boundary
// actually permits. The landing TAB keeps the name "Home" - a
// landing-tab convention.
export const DEVICES_ONLY_NAV: NavItem[] = [
  { path: "/", label: "Home", icon: HomeIcon },
  { path: "/devices", label: "Devices", icon: DevicesIcon },
  { path: "/history", label: "History", icon: HistoryIcon },
  { path: "/alerts", label: "Alerts", icon: BellIcon },
  { path: "/settings", label: "Settings", icon: SettingsIcon },
];

// The debug destinations - the cross-agent command debugger and the
// session inspector, occasional-use tools that earn a Settings row,
// not a tab (they briefly had their own mode; it merged back on
// 2026-08-29). Raw event payloads became a toggle on the Events feed
// rather than a destination at all.
export const DEBUG_LINKS: { path: string; label: string }[] = [
  { path: "/commands", label: "Commands" },
  { path: "/session", label: "Session" },
];

// The admin destinations - Developer Mode's Settings renders this list.
export const ADMIN_LINKS: { path: string; label: string }[] = [
  { path: "/admin/capabilities", label: "Capabilities" },
  { path: "/admin/device-types", label: "Device Types" },
  { path: "/admin/devices", label: "Devices" },
  { path: "/admin/agents", label: "Agents" },
  { path: "/admin/machines", label: "Machines" },
  { path: "/admin/agent-installations", label: "Agent Installations" },
  { path: "/admin/api-keys", label: "API Keys" },
];

export function isActivePath(location: string, path: string): boolean {
  if (path === "/") return location === "/";
  return location === path || location.startsWith(`${path}/`);
}

interface NavSidebarProps {
  items: NavItem[];
  // Which mode this full-access session is in - null for devicesOnly
  // keys, which have no mode. Shown as a badge since the two modes
  // share one nav and would otherwise be indistinguishable outside
  // Settings.
  mode?: "user" | "developer" | null;
}

export function ModeBadge({ mode }: { mode?: "user" | "developer" | null }) {
  // Only the elevated state earns a badge - User is the normal state
  // and labeling it would be noise (user call, 2026-08-29).
  if (mode !== "developer") return null;

  return <span className="mode-badge mode-badge-developer">Developer</span>;
}

// Desktop rendering. CSS (.app-shell-sidebar's media query) decides
// whether this or the tab bar is visible - both are always mounted.
export function NavSidebar({ items, mode }: NavSidebarProps) {
  const [location, navigate] = useLocation();

  return (
    <aside className="sidebar">
      {/* Two lines so nothing overflows the 230px sidebar: brand + the
          icon pair right-aligned, then the mode badge where the
          tenant/site line used to sit. No tenant/site here - that
          identity lives in Settings' Site section. */}
      <div className="sidebar-brand">
        <div className="sidebar-brand-row">
          <VivnestLogo className="sidebar-logo" />
          <span className="sidebar-title">Vivnest</span>
          <span className="sidebar-brand-icons">
            <ThemeToggle />
            <NotificationBell />
          </span>
        </div>
        <ModeBadge mode={mode} />
      </div>
      <nav className="sidebar-nav">
        {items.map(({ path, label, icon: Icon }) => (
          <button
            key={path}
            type="button"
            className={`sidebar-item${isActivePath(location, path) ? " active" : ""}`}
            onClick={() => navigate(path)}
          >
            <Icon className="sidebar-item-icon" />
            {label}
          </button>
        ))}
      </nav>
    </aside>
  );
}

interface NavTabBarProps {
  items: NavItem[];
}

// Narrow-viewport rendering of the same items.
export function NavTabBar({ items }: NavTabBarProps) {
  const [location, navigate] = useLocation();

  return (
    <nav className="bottom-tabs">
      {items.map(({ path, label, icon: Icon }) => (
        <button
          key={path}
          type="button"
          className={`bottom-tab${isActivePath(location, path) ? " active" : ""}`}
          onClick={() => navigate(path)}
        >
          <Icon className="bottom-tab-icon" />
          <span className="bottom-tab-label">{label}</span>
        </button>
      ))}
    </nav>
  );
}
