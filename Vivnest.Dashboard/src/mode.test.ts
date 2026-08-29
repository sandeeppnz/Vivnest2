import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import {
  clearStoredMode,
  getStoredMode,
  hasPin,
  hashPin,
  isValidPin,
  setPin,
  setStoredMode,
  verifyPin,
} from "./mode";

// vitest runs in plain node (no jsdom, deliberately - see plan D0);
// mode.ts touches localStorage only at call time, so a Map-backed stub
// is all these tests need.
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

    setStoredMode("home");
    expect(getStoredMode()).toBe("home");

    setStoredMode("developer");
    expect(getStoredMode()).toBe("developer");

    // Retired stored value from before the mode was renamed (see
    // mode.ts's naming history) - reads back as developer, never
    // bounces to the selector.
    localStorage.setItem("vivnest.mode", "installer");
    expect(getStoredMode()).toBe("developer");

    localStorage.setItem("vivnest.mode", "turbo");
    expect(getStoredMode()).toBeNull();

    clearStoredMode();
    expect(getStoredMode()).toBeNull();
  });
});

describe("the PIN", () => {
  it("verifies the right PIN and rejects the wrong one", async () => {
    expect(hasPin()).toBe(false);
    // No PIN set: nothing verifies - the gate can't be passed by
    // guessing on a fresh browser.
    expect(await verifyPin("1234")).toBe(false);

    await setPin("1234");
    expect(hasPin()).toBe(true);
    expect(await verifyPin("1234")).toBe(true);
    expect(await verifyPin("4321")).toBe(false);
  });

  it("stores a hash, not the PIN", async () => {
    await setPin("1234");
    const stored = localStorage.getItem("vivnest.homePinHash");
    expect(stored).not.toContain("1234");
    expect(stored).toMatch(/^[0-9a-f]{64}$/);
  });

  it("is salted - the hash is not a bare sha256 of the digits", async () => {
    // sha256("1234") - the first rainbow-table lookup anyone would try.
    const bare = "03ac674216f3e15c761ee1a5e255f067953623c8b388b4459e13f978d7c846f4";
    expect(await hashPin("1234")).not.toBe(bare);
  });

  it("accepts 4-8 digits and nothing else", () => {
    expect(isValidPin("1234")).toBe(true);
    expect(isValidPin("12345678")).toBe(true);
    expect(isValidPin("123")).toBe(false);
    expect(isValidPin("123456789")).toBe(false);
    expect(isValidPin("12a4")).toBe(false);
    expect(isValidPin("")).toBe(false);
  });
});
