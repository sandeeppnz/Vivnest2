import type { ReactElement } from "react";
import { AgentIcon, DevicesIcon, EventsIcon, OverviewIcon, type IconProps } from "./icons";

export type View = "overview" | "devices" | "agents" | "events";

interface BottomTabBarProps {
  active: View;
  onSelect: (view: View) => void;
}

const TABS: { view: View; label: string; icon: (props: IconProps) => ReactElement }[] = [
  { view: "overview", label: "Overview", icon: OverviewIcon },
  { view: "devices", label: "Devices", icon: DevicesIcon },
  { view: "agents", label: "Agents", icon: AgentIcon },
  { view: "events", label: "Events", icon: EventsIcon },
];

// Primary navigation on narrow viewports - fixed to the viewport bottom,
// per the original mobile-app mockup's design intent (Phase 9). At >=1024px
// the persistent Sidebar takes over and App.css hides this - both navs are
// always rendered; CSS decides which shows.
export function BottomTabBar({ active, onSelect }: BottomTabBarProps) {
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
