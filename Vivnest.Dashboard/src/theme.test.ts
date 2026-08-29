import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { getStoredTheme, resolveTheme } from "./theme";

// Same Map-backed localStorage stub as mode.test.ts (plain node, no
// jsdom). setTheme/applyStoredTheme touch document/matchMedia and are
// exercised in the browser instead; the pure parts are what's tested.
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

describe("theme storage", () => {
  it("defaults to dark and treats garbage as dark", () => {
    expect(getStoredTheme()).toBe("dark");

    localStorage.setItem("vivnest.theme", "neon");
    expect(getStoredTheme()).toBe("dark");
  });

  it("returns a stored light or system preference", () => {
    localStorage.setItem("vivnest.theme", "light");
    expect(getStoredTheme()).toBe("light");

    localStorage.setItem("vivnest.theme", "system");
    expect(getStoredTheme()).toBe("system");
  });
});

describe("resolveTheme", () => {
  it("passes fixed choices through regardless of the OS", () => {
    expect(resolveTheme("dark", true)).toBe("dark");
    expect(resolveTheme("dark", false)).toBe("dark");
    expect(resolveTheme("light", true)).toBe("light");
    expect(resolveTheme("light", false)).toBe("light");
  });

  it("resolves system from the OS preference", () => {
    expect(resolveTheme("system", true)).toBe("light");
    expect(resolveTheme("system", false)).toBe("dark");
  });
});
