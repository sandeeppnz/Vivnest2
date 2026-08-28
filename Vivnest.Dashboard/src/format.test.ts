import { afterEach, describe, expect, it, vi } from "vitest";
import { formatBytes, formatDateTime, formatInterval, formatUptime } from "./format";

// Seeded from real defects (dashboard-redesign-plan.md D0), not written
// for coverage: every case here is a shape that either shipped wrong
// once or exists to stop a specific regression.

describe("formatInterval", () => {
  // The bug that shipped: .NET's TimeSpan "c" format is
  // [d.]hh:mm:ss[.fffffff], and splitting on ":" alone read
  // "1.00:00:00" (24 hours) as one HOUR and rendered "60m".
  it("honours the days segment", () => {
    expect(formatInterval("1.00:00:00")).toBe("1d");
    expect(formatInterval("1.02:00:00")).toBe("26h");
  });

  it("floors fractional seconds", () => {
    expect(formatInterval("00:00:30.5000000")).toBe("30s");
  });

  it("renders whole units at the largest whole unit", () => {
    expect(formatInterval("01:00:00")).toBe("1h");
    expect(formatInterval("00:01:00")).toBe("1m");
    expect(formatInterval("00:01:30")).toBe("90s");
  });

  it("passes garbage through instead of rendering NaN text", () => {
    expect(formatInterval("garbage")).toBe("garbage");
    expect(formatInterval("aa:bb:cc")).toBe("aa:bb:cc");
  });

  it("renders zero as an em dash", () => {
    expect(formatInterval("00:00:00")).toBe("—");
  });
});

describe("formatDateTime", () => {
  afterEach(() => vi.useRealTimers());

  const at = (offsetSeconds: number) =>
    new Date(Date.now() + offsetSeconds * 1000).toISOString();

  // The papercut that shipped: a heartbeat stamped a few seconds "in
  // the future" by a server clock slightly ahead of the browser's
  // flipped to a full absolute date instead of reading "just now".
  it("tolerates up to a minute of clock skew", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-08-29T12:00:00Z"));

    expect(formatDateTime(at(10))).toBe("just now");
    expect(formatDateTime(at(-10))).toBe("just now");
  });

  it("still treats a genuinely future timestamp as absolute", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-08-29T12:00:00Z"));

    // Absolute format always contains the "date, time" comma.
    expect(formatDateTime(at(300))).toContain(",");
  });

  it("goes relative inside a week and absolute beyond it", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-08-29T12:00:00Z"));

    expect(formatDateTime(at(-3600))).toBe("1 hour ago");
    expect(formatDateTime(at(-2 * 24 * 3600))).toBe("2 days ago");
    expect(formatDateTime(at(-8 * 24 * 3600))).toContain(",");
  });
});

describe("formatUptime", () => {
  afterEach(() => vi.useRealTimers());

  it("renders as a duration, not a relative phrase", () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-08-29T12:00:00Z"));

    const startedAt = (secondsAgo: number) =>
      new Date(Date.now() - secondsAgo * 1000).toISOString();

    expect(formatUptime(startedAt(30))).toBe("just started");
    expect(formatUptime(startedAt(3 * 86400 + 5 * 3600))).toBe("3d 5h");
    expect(formatUptime(startedAt(2 * 3600 + 15 * 60))).toBe("2h 15m");
  });
});

describe("formatBytes", () => {
  it("steps through the units", () => {
    expect(formatBytes(512)).toBe("512 B");
    expect(formatBytes(2048)).toBe("2.0 KB");
    expect(formatBytes(5 * 1024 * 1024)).toBe("5.0 MB");
  });
});
