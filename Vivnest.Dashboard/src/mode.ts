// Mode selection + the Home Mode PIN (the 2026-08-07 mockup's
// "Pick your mode" screen and PIN-gated developer unlock).
//
// Honest scope: this is CHILD-PROOFING, not a security boundary. The
// tenant key in localStorage already has full API access regardless of
// mode, and anything client-side can be edited by whoever owns the
// browser. The gate exists so a household member in Home Mode doesn't
// wander into Deploy buttons - the same job as a TV's parental PIN.
// A real permission boundary is the devicesOnly KEY (server-enforced),
// which is why devicesOnly sessions are locked to Home Mode with no
// unlock row at all.

// Naming history, so the migration below reads sanely: the full mode
// was born "installer" (the mockup's imagined professional-installer
// persona), briefly had a separate "developer" sibling, absorbed it on
// 2026-08-29, and was then RENAMED to Developer the same day - the
// person actually using the full tool is a developer, not an
// installer. "developer" is the canonical stored value; "installer" is
// the retired one.
export type DashboardMode = "home" | "developer";

const MODE_KEY = "vivnest.mode";
const PIN_HASH_KEY = "vivnest.homePinHash";

// Domain-separates the hash from a bare sha256(pin) rainbow lookup;
// deliberately NOT a secret (see the child-proofing note above).
const PIN_SALT = "vivnest-home-pin-v1:";

export function getStoredMode(): DashboardMode | null {
  const value = localStorage.getItem(MODE_KEY);

  // Retired stored value (see the naming history above). Browsers that
  // stored it keep working instead of being bounced to the selector.
  if (value === "installer") return "developer";

  return value === "home" || value === "developer" ? value : null;
}

export function setStoredMode(mode: DashboardMode): void {
  localStorage.setItem(MODE_KEY, mode);
}

export function clearStoredMode(): void {
  localStorage.removeItem(MODE_KEY);
}

export function hasPin(): boolean {
  return localStorage.getItem(PIN_HASH_KEY) !== null;
}

export async function hashPin(pin: string): Promise<string> {
  const bytes = new TextEncoder().encode(PIN_SALT + pin);
  const digest = await crypto.subtle.digest("SHA-256", bytes);

  return Array.from(new Uint8Array(digest))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

export async function setPin(pin: string): Promise<void> {
  localStorage.setItem(PIN_HASH_KEY, await hashPin(pin));
}

export async function verifyPin(pin: string): Promise<boolean> {
  const stored = localStorage.getItem(PIN_HASH_KEY);
  if (!stored) return false;

  return (await hashPin(pin)) === stored;
}

export function isValidPin(pin: string): boolean {
  return /^\d{4,8}$/.test(pin);
}
