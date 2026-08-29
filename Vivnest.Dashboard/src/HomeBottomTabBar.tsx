import type { ReactElement } from "react";
import { DevicesIcon, HistoryIcon, HomeIcon, SettingsIcon, type IconProps } from "./icons";

// Home Mode's primary navigation (dashboard-redesign-plan.md D4) -
// the 2026-08-07 mockup's Home / Devices / History / Alerts / Settings
// bar, minus Alerts, which stays deferred with the rest of the
// notification work. Rendered only for devicesOnly sessions; the
// full-access app keeps BottomTabBar/Sidebar untouched.
export type HomeView = "home" | "devices" | "history" | "settings";

interface HomeBottomTabBarProps {
  active: HomeView;
  onSelect: (view: HomeView) => void;
}

const TABS: { view: HomeView; label: string; icon: (props: IconProps) => ReactElement }[] = [
  { view: "home", label: "Home", icon: HomeIcon },
  { view: "devices", label: "Devices", icon: DevicesIcon },
  { view: "history", label: "History", icon: HistoryIcon },
  { view: "settings", label: "Settings", icon: SettingsIcon },
];

export function HomeBottomTabBar({ active, onSelect }: HomeBottomTabBarProps) {
  return (
    <nav className="bottom-tabs">
      {TABS.map(({ view, label, icon: Icon }) => (
        <button
          key={view}
          type="button"
          className={`bottom-tab${active === view ? " active" : ""}`}
          onClick={() => onSelect(view)}
        >
          <Icon className="bottom-tab-icon" />
          <span className="bottom-tab-label">{label}</span>
        </button>
      ))}
    </nav>
  );
}
