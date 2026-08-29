// Mode selection (the 2026-08-07 mockup's "Pick your mode" screen).
//
// The mode is a LENS, not a boundary: since roles-on-keys (ADR-118)
// the User/Developer split is enforced server-side by the key's Role,
// and a developer-role key switches modes freely - User Mode is just
// the quiet view of the same key. There used to be a child-proofing
// PIN gating the User -> Developer switch; roles obsoleted it (the way
// to protect a shared browser is a user-role key, which the server
// refuses admin/actions for regardless of anything client-side) and it
// was removed the same day it was built. A stale
// "vivnest.homePinHash" localStorage entry may linger on browsers that
// set one - harmless, nothing reads it.

// Naming history, so the migrations below read sanely (all renames
// happened 2026-08-29): the full mode was born "installer" (the
// mockup's imagined professional-installer persona), briefly had a
// separate "developer" sibling, absorbed it, and was then RENAMED to
// Developer. The simple mode was born "home" (the mockup's household
// framing) and RENAMED to User - Vivnest targets more verticals than
// homes. Canonical stored values: "user" and "developer"; "home" and
// "installer" are retired.
export type DashboardMode = "user" | "developer";

const MODE_KEY = "vivnest.mode";

export function getStoredMode(): DashboardMode | null {
  const value = localStorage.getItem(MODE_KEY);

  // Retired stored values (see the naming history above). Browsers
  // that stored them keep working instead of being bounced to the
  // selector.
  if (value === "installer") return "developer";
  if (value === "home") return "user";

  return value === "user" || value === "developer" ? value : null;
}

export function setStoredMode(mode: DashboardMode): void {
  localStorage.setItem(MODE_KEY, mode);
}

export function clearStoredMode(): void {
  localStorage.removeItem(MODE_KEY);
}
