import { MoonIcon, SunIcon } from "./icons";
import { setTheme, useResolvedTheme } from "./theme";

// One-tap dark/light flip beside the bell. Deliberately writes an
// EXPLICIT preference (never "system") - a person reaching for the
// toggle wants this look now, not "whatever the OS says"; Settings'
// Appearance chips remain the way back to System. The icon shows the
// theme a click switches TO.
export function ThemeToggle() {
  const resolved = useResolvedTheme();

  return (
    <button
      type="button"
      className="notification-bell"
      onClick={() => setTheme(resolved === "dark" ? "light" : "dark")}
      aria-label={resolved === "dark" ? "Switch to light theme" : "Switch to dark theme"}
    >
      {resolved === "dark" ? (
        <SunIcon className="notification-bell-icon" />
      ) : (
        <MoonIcon className="notification-bell-icon" />
      )}
    </button>
  );
}
