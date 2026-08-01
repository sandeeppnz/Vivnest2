const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:7071/api";

export interface DeviceSummary {
  deviceId: string;
  deviceType: string;
  status: string;
  lastHeartbeatUtc: string;
  lastActivityUtc: string | null;
  error: string | null;
}

export interface DeviceEvent {
  eventType: string;
  severity: string;
  occurredAtUtc: string;
  data: unknown;
  imageUrl: string | null;
}

export interface AgentSummary {
  agentId: string;
  hostName: string;
  status: string;
  startedUtc: string;
  lastHeartbeatUtc: string;
  heartbeatInterval: string;
  error: string | null;
}

export class ApiError extends Error {
  status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

async function request<T>(path: string, apiKey: string): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    headers: { "x-api-key": apiKey },
  });

  if (response.status === 401) {
    throw new ApiError(401, "Invalid API key.");
  }

  if (response.status === 404) {
    throw new ApiError(404, "Not found.");
  }

  if (!response.ok) {
    throw new ApiError(response.status, `Request failed (${response.status}).`);
  }

  return (await response.json()) as T;
}

export function getDevices(apiKey: string): Promise<DeviceSummary[]> {
  return request<DeviceSummary[]>("/devices", apiKey);
}

export function getDevice(apiKey: string, deviceId: string): Promise<DeviceSummary> {
  return request<DeviceSummary>(`/devices/${encodeURIComponent(deviceId)}`, apiKey);
}

export function getDeviceEvents(apiKey: string, deviceId: string, take = 50): Promise<DeviceEvent[]> {
  return request<DeviceEvent[]>(
    `/devices/${encodeURIComponent(deviceId)}/events?take=${take}`,
    apiKey,
  );
}

export function getDeviceCaptures(apiKey: string, deviceId: string, take = 20): Promise<DeviceEvent[]> {
  return request<DeviceEvent[]>(
    `/devices/${encodeURIComponent(deviceId)}/captures?take=${take}`,
    apiKey,
  );
}

export function getDeviceCapturesTimeline(apiKey: string, deviceId: string, days = 30): Promise<DeviceEvent[]> {
  return request<DeviceEvent[]>(
    `/devices/${encodeURIComponent(deviceId)}/captures?days=${days}`,
    apiKey,
  );
}

export function getAgents(apiKey: string): Promise<AgentSummary[]> {
  return request<AgentSummary[]>("/agents", apiKey);
}
