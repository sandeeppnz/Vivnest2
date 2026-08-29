import { useState } from "react";
import { VivnestLogo } from "./icons";
import { hasPin, isValidPin, setPin, setStoredMode, type DashboardMode } from "./mode";

interface ModeSelectProps {
  onSelected: (mode: DashboardMode) => void;
}

// The mockup's "Pick your mode" landing screen, shown once per browser
// for a full-access key (devicesOnly keys get the trimmed device view
// and never see this). Both modes share the same nav; the choice is
// whether Admin + Debug are open (Developer) or PIN-gated (User).
// Choosing User sets the unlock PIN in the same flow - a User Mode
// without a PIN would make the gate a plain tap, which is no gate at
// all. Developer Mode enters freely - the PIN only guards the unlock
// from within User Mode.
export function ModeSelect({ onSelected }: ModeSelectProps) {
  const [settingPin, setSettingPin] = useState(false);
  const [pin, setPinValue] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function chooseUser() {
    // A PIN from an earlier session on this browser still counts - don't
    // force a reset just for re-picking the mode.
    if (hasPin()) {
      setStoredMode("user");
      onSelected("user");
      return;
    }

    setSettingPin(true);
  }

  async function saveUserPin() {
    if (!isValidPin(pin)) {
      setError("The PIN must be 4 to 8 digits.");
      return;
    }
    if (pin !== confirm) {
      setError("The PINs don't match.");
      return;
    }

    await setPin(pin);
    setStoredMode("user");
    onSelected("user");
  }

  return (
    <div className="mode-select">
      <VivnestLogo className="mode-select-logo" />
      <h1>Pick your mode</h1>

      {!settingPin ? (
        <div className="mode-select-cards">
          <button type="button" className="mode-select-card" onClick={chooseUser}>
            <span className="mode-select-card-title">User</span>
            <span className="mode-select-card-text">
              The full dashboard - agents, devices and events. Admin and debug tools stay behind a PIN.
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
              Everything, including admin registries, configuration publishing and debug tools.
            </span>
          </button>
        </div>
      ) : (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void saveUserPin();
          }}
        >
          <p>Set a PIN to unlock admin and debug tools later.</p>
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
          <button type="submit">Continue in User Mode</button>
          <button type="button" className="link-button" onClick={() => setSettingPin(false)}>
            Back
          </button>
          {error && <p className="form-dialog-error">{error}</p>}
        </form>
      )}
    </div>
  );
}
