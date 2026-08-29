import { useSyncExternalStore } from "react";
import type { DeviceEvent } from "./api";
import { isDuplicatedElsewhere } from "./eventDescriptions";

// The shared alert definition and seen-stamp, used by both the bell
// (NotificationBell.tsx) and the Alerts screen (AlertsPage.tsx) - one
// store, so reading alerts in either place clears the other's badge.
//
// "Read" state is localStorage BY DESIGN, not an oversight: users don't
// exist server-side (one shared tenant key), so there is nowhere to
// sync it to - see deferred-notification-center.

const SEEN_KEY = "vivnest.alertsSeenUtc";

const ALERT_SEVERITIES = new Set(["Warning", "Critical"]);

export function isAlertEvent(event: DeviceEvent): boolean {
  return !isDuplicatedElsewhere(event) && ALERT_SEVERITIES.has(event.severity);
}

// Module-level store with subscribers: multiple consumers exist (two
// bell instances plus the Alerts screen), and marking seen in one must
// clear the badge on all of them immediately.
let seenUtcCache = ((): number => {
  const stored = localStorage.getItem(SEEN_KEY);
  return stored ? Date.parse(stored) : 0;
})();

const seenListeners = new Set<() => void>();

function getSeenUtc(): number {
  return seenUtcCache;
}

function subscribeSeen(listener: () => void): () => void {
  seenListeners.add(listener);
  return () => seenListeners.delete(listener);
}

export function useAlertsSeenUtc(): number {
  return useSyncExternalStore(subscribeSeen, getSeenUtc);
}

export function markAlertsSeen(): void {
  const now = new Date().toISOString();
  localStorage.setItem(SEEN_KEY, now);
  seenUtcCache = Date.parse(now);
  for (const listener of seenListeners) listener();
}
