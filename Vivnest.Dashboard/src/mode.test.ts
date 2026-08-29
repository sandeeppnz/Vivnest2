import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { clearStoredMode, getStoredMode, setStoredMode } from "./mode";

// vitest runs in plain node (no jsdom, deliberately - see plan D0);
// mode.ts touches localStorage only at call time, so a Map-backed stub
// is all these tests need. (The PIN tests that used to live here went
// with the PIN itself - roles-on-keys, ADR-118, made it redundant.)
const store = new Map<string, string>();

beforeAll(() => {
  vi.stubGlobal("localStorage", {
    getItem: (k: string) => store.get(k) ?? null,
    setItem: (k: string, v: string) => void store.set(k, String(v)),
    removeItem: (k: string) => void store.delete(k),
    clear: () => store.clear(),
  });
});

afterEach(() => localStorage.clear());

describe("mode storage", () => {
  it("round-trips a chosen mode and treats garbage as unchosen", () => {
    expect(getStoredMode()).toBeNull();

    setStoredMode("user");
    expect(getStoredMode()).toBe("user");

    setStoredMode("developer");
    expect(getStoredMode()).toBe("developer");

    // Retired stored values from before the modes were renamed (see
    // mode.ts's naming history) - they read back as the current names,
    // never bouncing to the selector.
    localStorage.setItem("vivnest.mode", "installer");
    expect(getStoredMode()).toBe("developer");

    localStorage.setItem("vivnest.mode", "home");
    expect(getStoredMode()).toBe("user");

    localStorage.setItem("vivnest.mode", "turbo");
    expect(getStoredMode()).toBeNull();

    clearStoredMode();
    expect(getStoredMode()).toBeNull();
  });
});
