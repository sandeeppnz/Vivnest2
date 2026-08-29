// Theme preference: dark (the default - today's look), light, or
// system (follow the OS). Resolution happens in JS, not CSS media
// queries: main.tsx stamps data-theme="dark"|"light" on <html> before
// first render, and every stylesheet keys off that one attribute -
// index.css used to follow prefers-color-scheme on its own, which
// meant heading colors could disagree with the app's fixed-dark
// palette on a light-OS machine.
import { useSyncExternalStore } from "react";

export type ThemePreference = "dark" | "light" | "system";

const THEME_KEY = "vivnest.theme";

const LIGHT_QUERY = "(prefers-color-scheme: light)";

export function getStoredTheme(): ThemePreference {
  const value = localStorage.getItem(THEME_KEY);
  return value === "light" || value === "system" ? value : "dark";
}

export function resolveTheme(
  preference: ThemePreference,
  systemPrefersLight: boolean,
): "dark" | "light" {
  if (preference === "system") return systemPrefersLight ? "light" : "dark";
  return preference;
}

// Subscription store so multiple consumers stay in sync: the header
// toggle and Settings' Appearance chips both render theme state, and
// changing it in one place must update the other immediately. No
// module-level localStorage reads - vitest runs in plain node and
// stubs localStorage after import.
const themeListeners = new Set<() => void>();

let resolvedCache: "dark" | "light" = "dark";

function subscribeTheme(listener: () => void): () => void {
  themeListeners.add(listener);
  return () => themeListeners.delete(listener);
}

// The stored preference (dark/light/system), live.
export function useThemePreference(): ThemePreference {
  return useSyncExternalStore(subscribeTheme, getStoredTheme);
}

// What's actually on screen (system resolved), live - drives the
// toggle's sun/moon icon.
export function useResolvedTheme(): "dark" | "light" {
  return useSyncExternalStore(subscribeTheme, () => resolvedCache);
}

function apply(preference: ThemePreference): void {
  resolvedCache = resolveTheme(preference, window.matchMedia(LIGHT_QUERY).matches);
  document.documentElement.dataset.theme = resolvedCache;
  for (const listener of themeListeners) listener();
}

export function setTheme(preference: ThemePreference): void {
  localStorage.setItem(THEME_KEY, preference);
  apply(preference);
}

// Called once at startup. The change listener only matters for
// "system": an OS theme flip must re-resolve without a reload, and for
// the fixed choices it deliberately does nothing.
export function applyStoredTheme(): void {
  apply(getStoredTheme());

  window.matchMedia(LIGHT_QUERY).addEventListener("change", () => {
    if (getStoredTheme() === "system") apply("system");
  });
}
