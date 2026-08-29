// Mode selection + the User Mode PIN (the 2026-08-07 mockup's
// "Pick your mode" screen and PIN-gated developer unlock).
//
// Honest scope: this is CHILD-PROOFING, not a security boundary. The
// tenant key in localStorage already has full API access regardless of
// mode, and anything client-side can be edited by whoever owns the
// browser. Since 2026-08-29 the modes share the same nav and the gate
// guards Settings' Admin + Debug sections (and their routes) plus the
// in-page actions on Agent/Device detail (Restart, Download logs,
// Refresh/Apply/Deploy, the embedded publish/rollback panels) - the
// same job as a TV's parental PIN. User Mode sees every page
// read-only; Capture now stays for everyone as a user-facing feature.
// A real permission boundary is the devicesOnly KEY (server-enforced),
// which is why devicesOnly sessions get the trimmed device view with
// no mode selector or unlock row at all.

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

// The stored KEY and SALT keep their original "home" names on purpose:
// the key renames read-time-migratably, but a salt change would
// silently invalidate every PIN already set, and a key rename would
// orphan them. Wire/storage constants outlive their names - same call
// as the JSON "capabilityId" precedent.
const PIN_HASH_KEY = "vivnest.homePinHash";

// Domain-separates the hash from a bare sha256(pin) rainbow lookup;
// deliberately NOT a secret (see the child-proofing note above).
const PIN_SALT = "vivnest-home-pin-v1:";

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
