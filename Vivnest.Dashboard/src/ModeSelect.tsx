import { VivnestLogo } from "./icons";
import { setStoredMode, type DashboardMode } from "./mode";

interface ModeSelectProps {
  onSelected: (mode: DashboardMode) => void;
}

// The mockup's "Pick your mode" landing screen, shown once per browser
// for a developer-role key (devicesOnly keys get the trimmed device
// view, user-role keys are locked to User Mode by the server - neither
// ever sees this). Both modes share the same nav; the choice is purely
// how much the chrome shows, and switching later is free in both
// directions - the server's role gates are what actually protect the
// admin/action surface (ADR-118), so there is no PIN.
export function ModeSelect({ onSelected }: ModeSelectProps) {
  function choose(mode: DashboardMode) {
    setStoredMode(mode);
    onSelected(mode);
  }

  return (
    <div className="mode-select">
      <VivnestLogo className="mode-select-logo" />
      <h1>Pick your mode</h1>

      <div className="mode-select-cards">
        <button type="button" className="mode-select-card" onClick={() => choose("user")}>
          <span className="mode-select-card-title">User</span>
          <span className="mode-select-card-text">
            The full dashboard - agents, devices and events. Admin and debug tools stay tucked away.
          </span>
        </button>
        <button type="button" className="mode-select-card" onClick={() => choose("developer")}>
          <span className="mode-select-card-title">Developer</span>
          <span className="mode-select-card-text">
            Everything, including admin registries, configuration publishing and debug tools.
          </span>
        </button>
      </div>
    </div>
  );
}
