import type { Settings } from "./storage";

// The three Android channels Cloud routes to (ExpoPushNotificationChannel).
export type AlertChannel = "fire-alarm" | "alerts" | "info";

export interface RegisteredPhone {
  pushDeviceId: string;
  name: string;
  platform: string;
  enabled: boolean;
}

export interface TestResult {
  phones: number;
  accepted: number;
}

async function request<T>(settings: Settings, path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${settings.apiUrl.replace(/\/+$/, "")}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      "x-api-key": settings.apiKey,
      ...(init?.headers ?? {}),
    },
  });

  if (response.status === 401) {
    throw new Error("The server rejected the API key.");
  }

  if (!response.ok) {
    const text = await response.text().catch(() => "");
    throw new Error(`Server returned ${response.status}${text ? `: ${text.slice(0, 160)}` : ""}`);
  }

  // DELETE returns 204 with no body.
  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

// Idempotent on the server: the same token updates one row, so this is
// safe to call on every launch.
export function registerPhone(
  settings: Settings,
  expoPushToken: string,
  platform: string,
): Promise<RegisteredPhone> {
  return request<RegisteredPhone>(settings, "/push-devices", {
    method: "POST",
    body: JSON.stringify({ expoPushToken, name: settings.phoneName, platform }),
  });
}

export function unregisterPhone(settings: Settings, pushDeviceId: string): Promise<void> {
  return request<void>(settings, `/push-devices/${encodeURIComponent(pushDeviceId)}`, {
    method: "DELETE",
  });
}

export function sendTestAlert(settings: Settings, channel: AlertChannel): Promise<TestResult> {
  return request<TestResult>(settings, "/push-devices/test", {
    method: "POST",
    body: JSON.stringify({ channel }),
  });
}
