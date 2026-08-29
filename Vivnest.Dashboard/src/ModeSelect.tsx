import { useState } from "react";
import { VivnestLogo } from "./icons";
import { hasPin, isValidPin, setPin, setStoredMode, type DashboardMode } from "./mode";

interface ModeSelectProps {
  onSelected: (mode: DashboardMode) => void;
}

// The mockup's "Pick your mode" landing screen, shown once per browser
// for a full-access key (devicesOnly keys are locked to Home Mode and
// never see this). Choosing Home sets the unlock PIN in the same flow -
// a Home Mode without a PIN would make the installer gate a plain tap,
// which is no gate at all. Developer Mode enters freely like
// Installer - the PIN only guards leaving Home.
export function ModeSelect({ onSelected }: ModeSelectProps) {
  const [settingPin, setSettingPin] = useState(false);
  const [pin, setPinValue] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function chooseHome() {
    // A PIN from an earlier session on this browser still counts - don't
    // force a reset just for re-picking the mode.
    if (hasPin()) {
      setStoredMode("home");
      onSelected("home");
      return;
    }

    setSettingPin(true);
  }

  async function saveHomePin() {
    if (!isValidPin(pin)) {
      setError("The PIN must be 4 to 8 digits.");
      return;
    }
    if (pin !== confirm) {
      setError("The PINs don't match.");
      return;
    }

    await setPin(pin);
    setStoredMode("home");
    onSelected("home");
  }

  return (
    <div className="mode-select">
      <VivnestLogo className="mode-select-logo" />
      <h1>Pick your mode</h1>

      {!settingPin ? (
        <div className="mode-select-cards">
          <button type="button" className="mode-select-card" onClick={chooseHome}>
            <span className="mode-select-card-title">Home</span>
            <span className="mode-select-card-text">
              Devices, history and alerts - the household view. Installer features stay behind a PIN.
            </span>
          </button>
          <button
            type="button"
            className="mode-select-card"
            onClick={() => {
              setStoredMode("installer");
              onSelected("installer");
            }}
          >
            <span className="mode-select-card-title">Installer</span>
            <span className="mode-select-card-text">
              Agents, deploys, configuration publishing and the admin registries.
            </span>
          </button>
          <button
            type="button"
            className="mode-select-card"
            onClick={() => {
              setStoredMode("developer");
              onSelected("developer");
            }}
          >
            <span className="mode-select-card-title">Developer</span>
            <span className="mode-select-card-text">
              Cross-agent command debugging, raw event payloads and session internals.
            </span>
          </button>
        </div>
      ) : (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void saveHomePin();
          }}
        >
          <p>Set a PIN to unlock Installer features later.</p>
          <p className="form-hint">
            This is child-proofing on this browser, not account security - anyone with the key can
            still use the API directly.
          </p>
          <input
            type="password"
            inputMode="numeric"
            value={pin}
            onChange={(e) => setPinValue(e.target.value)}
            placeholder="PIN (4-8 digits)"
            autoFocus
          />
          <input
            type="password"
            inputMode="numeric"
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
            placeholder="Confirm PIN"
          />
          <button type="submit">Use Home Mode</button>
          <button type="button" className="link-button" onClick={() => setSettingPin(false)}>
            Back
          </button>
          {error && <p className="form-dialog-error">{error}</p>}
        </form>
      )}
    </div>
  );
}
