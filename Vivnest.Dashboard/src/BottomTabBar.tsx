import type { ReactElement } from "react";
import { AgentIcon, DevicesIcon, OverviewIcon, type IconProps } from "./icons";

export type View = "overview" | "devices" | "agents";

interface BottomTabBarProps {
  active: View;
  onSelect: (view: View) => void;
}

const TABS: { view: View; label: string; icon: (props: IconProps) => ReactElement }[] = [
  { view: "overview", label: "Overview", icon: OverviewIcon },
  { view: "devices", label: "Devices", icon: DevicesIcon },
  { view: "agents", label: "Agents", icon: AgentIcon },
];

// Primary navigation - fixed to the viewport bottom at every width, per the
// original mobile-app mockup's design intent (Phase 9). Deliberately not
// swapped for a top nav on wider screens: one nav treatment everywhere,
// tried first before adding a second responsive variant.
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
