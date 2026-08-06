const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:7071/api";

export interface DeviceSummary {
  deviceId: string;
  name: string;
  deviceType: string;
  status: string;
  statusSinceUtc: string | null;
  lastHeartbeatUtc: string;
  lastActivityUtc: string | null;
  heartbeatInterval: string;
  agentId: string;
  tenantId: string;
  siteId: string;
  error: string | null;
  parentDeviceId: string | null;
}

export interface DeviceEvent {
  eventType: string;
  severity: string;
  occurredAtUtc: string;
  data: unknown;
  imageUrl: string | null;
}

export interface CaptureDaySummary {
  date: string;
  count: number;
}

export interface CapturePage {
  captures: DeviceEvent[];
  hasMore: boolean;
}

export interface AgentSummary {
  agentId: string;
  name: string;
  hostName: string;
  firmwareVersion: string;
  runtimeVersion: string;
  osDescription: string;
  status: string;
  startedUtc: string;
  lastHeartbeatUtc: string;
  heartbeatInterval: string;
  statusSinceUtc: string;
  tenantId: string;
  siteId: string;
  error: string | null;
}

export interface AgentMetricSample {
  occurredAtUtc: string;
  cpuUsagePercent: number | null;
  memoryUsedBytes: number;
  bytesUploaded: number;
}

export interface AgentLogs {
  url: string;
}

export interface WhoAmI {
  tenantId: string;
  siteId: string;
  devicesOnly: boolean;
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

export function getDeviceBattery(apiKey: string, deviceId: string, take = 30): Promise<DeviceEvent[]> {
  return request<DeviceEvent[]>(
    `/devices/${encodeURIComponent(deviceId)}/battery?take=${take}`,
    apiKey,
  );
}

export function getDeviceCaptureDaySummaries(
  apiKey: string,
  deviceId: string,
  days = 30,
): Promise<CaptureDaySummary[]> {
  return request<CaptureDaySummary[]>(
    `/devices/${encodeURIComponent(deviceId)}/captures/summary?days=${days}`,
    apiKey,
  );
}

export function getDeviceCapturesByDay(
  apiKey: string,
  deviceId: string,
  date: string,
  skip: number,
  take = 50,
): Promise<CapturePage> {
  return request<CapturePage>(
    `/devices/${encodeURIComponent(deviceId)}/captures?date=${date}&skip=${skip}&take=${take}`,
    apiKey,
  );
}

export function getAgents(apiKey: string): Promise<AgentSummary[]> {
  return request<AgentSummary[]>("/agents", apiKey);
}

export function getAgent(apiKey: string, agentId: string): Promise<AgentSummary> {
  return request<AgentSummary>(`/agents/${encodeURIComponent(agentId)}`, apiKey);
}

export function getAgentMetrics(
  apiKey: string,
  agentId: string,
  days = 30,
): Promise<AgentMetricSample[]> {
  return request<AgentMetricSample[]>(
    `/agents/${encodeURIComponent(agentId)}/metrics?days=${days}`,
    apiKey,
  );
}

// The dashboard's first mutating request - every other call here is a GET.
// Doesn't reuse request<T>() since the endpoint returns 202 Accepted with
// no JSON body to parse.
export async function restartAgent(apiKey: string, agentId: string): Promise<void> {
  const response = await fetch(
    `${API_BASE_URL}/agents/${encodeURIComponent(agentId)}/restart`,
    {
      method: "POST",
      headers: { "x-api-key": apiKey },
    },
  );

  if (response.status === 401) {
    throw new ApiError(401, "Invalid API key.");
  }

  if (response.status === 403) {
    throw new ApiError(403, "Not permitted.");
  }

  if (response.status === 404) {
    throw new ApiError(404, "Not found.");
  }

  if (!response.ok) {
    throw new ApiError(response.status, `Request failed (${response.status}).`);
  }
}

// Same shape as restartAgent - 202 Accepted, no JSON body to parse.
export async function deployAgent(apiKey: string, agentId: string): Promise<void> {
  const response = await fetch(
    `${API_BASE_URL}/agents/${encodeURIComponent(agentId)}/deploy`,
    {
      method: "POST",
      headers: { "x-api-key": apiKey },
    },
  );

  if (response.status === 401) {
    throw new ApiError(401, "Invalid API key.");
  }

  if (response.status === 403) {
    throw new ApiError(403, "Not permitted.");
  }

  if (response.status === 404) {
    throw new ApiError(404, "Not found.");
  }

  if (!response.ok) {
    throw new ApiError(response.status, `Request failed (${response.status}).`);
  }
}

export function getWhoAmI(apiKey: string): Promise<WhoAmI> {
  return request<WhoAmI>("/whoami", apiKey);
}

export function getAgentLogs(apiKey: string, agentId: string): Promise<AgentLogs> {
  return request<AgentLogs>(`/agents/${encodeURIComponent(agentId)}/logs`, apiKey);
}
