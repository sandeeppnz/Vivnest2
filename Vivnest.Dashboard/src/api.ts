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
  timezone: string;
  location: string;
  brand: string;
  model: string;
  firmware: string;
  thumbnailUrl: string | null;
  sinkCleanlinessEnabled: boolean;
  objectDetectionEnabled: boolean;
}

export interface DetectedObject {
  className: string;
  confidence: number;
  x1: number;
  y1: number;
  x2: number;
  y2: number;
  unusual: boolean;
}

export interface DeviceEvent {
  deviceId: string;
  deviceType: string;
  eventType: string;
  severity: string;
  occurredAtUtc: string;
  data: unknown;
  imageUrl: string | null;
  // Only populated on captures returned by getDeviceCapturesByDay - null
  // means this photo was never classified, not that it's dirty.
  sinkCleanlinessResult: boolean | null;
  // Same - null means ObjectDetection never ran on this capture, not "ran
  // and found nothing" (that's an empty array).
  detectedObjects: DetectedObject[] | null;
}

export interface CaptureDaySummary {
  date: string;
  count: number;
}

export interface CapturePage {
  captures: DeviceEvent[];
  hasMore: boolean;
}

// A capability (e.g. "Image Classification") is a fixed, canonical concept,
// distinct from whatever device/service actually provides it (its
// services list) - see decision-log.md ADR-041. Every capability today
// happens to have exactly one service, but the shape doesn't assume that.
export interface Capability {
  name: string;
  source: string;
  services: CapabilityService[];
}

export interface CapabilityService {
  name: string;
  enabled: boolean;
  executingAgentId: string | null;
  roiLeft: number | null;
  roiTop: number | null;
  roiRight: number | null;
  roiBottom: number | null;
  host: string | null;
  username: string | null;
  modelPath: string | null;
  confidenceThreshold: number | null;
  livenessInterval: string | null;
  warningMultiplier: number | null;
}

export interface TriggeredBy {
  deviceId: string;
  deviceName: string;
  deviceType: string;
}

export interface SourceSensor {
  name: string;
  accessible: boolean;
  inaccessibleReason: string | null;
  usedByCount: number;
}

export interface DeviceCapabilities {
  capabilities: Capability[];
  triggeredBy: TriggeredBy[];
  sourceSensors: SourceSensor[];
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

// Admin > Capabilities master-list record (decision-log.md ADR-042) -
// deliberately unrelated to the read-only per-device Capability/
// CapabilityService types above (ADR-040/041) - different concept,
// different lifecycle, kept separate on purpose.
export type CapabilityType = "BuiltIn" | "Derived" | "System";

export interface CapabilityAdmin {
  capabilityId: string;
  capabilityName: string;
  capabilityType: CapabilityType;
}

// Admin > Agents pre-registration record (decision-log.md ADR-043) -
// deliberately unrelated to AgentSummary above, which reflects real, live
// heartbeat data. Registering an agent here just reserves its identity
// for whoever sets up the physical device later.
export type AgentRegistryType = "Low" | "High";

export interface AgentRegistry {
  agentId: string;
  name: string;
  firmwareVersion: string;
  type: AgentRegistryType;
  tenantId: string;
  siteId: string;
  capabilityIds: string[];
}

// Admin > Device Types master-list record (decision-log.md ADR-047) -
// deliberately unrelated to the fixed DeviceType the real per-device
// Capabilities tab / Vivnest.Core.Enums.DeviceType uses. Same split as
// CapabilityAdmin vs. the read-only Capability/CapabilityService types.
export interface DeviceTypeAdmin {
  deviceTypeId: string;
  deviceTypeName: string;
}

// Admin > Devices pre-registration record (decision-log.md ADR-048) -
// deliberately unrelated to Device above, which reflects real, live
// heartbeat data. Registering a device here just reserves its identity
// and declares planned facts - it does not configure a real device; the
// device-config blob workflow is unchanged. Settings is non-secret
// connection facts only (Host, Username, ...) - never credentials, see
// DeviceRegistryFormModal's own warning text.
export interface DeviceRegistry {
  deviceId: string;
  name: string;
  deviceTypeId: string;
  owningAgentId: string;
  location: string;
  brand: string;
  model: string;
  firmware: string;
  enabled: boolean;
  capabilityIds: string[];
  settings: Record<string, string>;
  tenantId: string;
  siteId: string;
}

export class ApiError extends Error {
  status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

interface RequestOptions {
  method?: string;
  body?: unknown;
}

async function request<T>(path: string, apiKey: string, options?: RequestOptions): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    method: options?.method,
    headers: {
      "x-api-key": apiKey,
      ...(options?.body !== undefined ? { "Content-Type": "application/json" } : {}),
    },
    body: options?.body !== undefined ? JSON.stringify(options.body) : undefined,
  });

  if (response.status === 401) {
    throw new ApiError(401, "Invalid API key.");
  }

  if (response.status === 404) {
    throw new ApiError(404, "Not found.");
  }

  if (!response.ok) {
    throw new ApiError(response.status, await readErrorMessage(response));
  }

  return (await response.json()) as T;
}

// BadRequestObjectResult("some message") on this stack serializes as
// Content-Type: text/plain with the message as the raw body - not a JSON
// string - confirmed directly against a real 400 response (the Settings
// credential-guard message came back as plain text, unquoted). This
// surfaces that real, specific reason instead of a generic
// "Request failed (400)" that hides why. If the body happens to be JSON
// (e.g. a future endpoint returns an object), unwrap a plain string but
// otherwise fall back to the generic message - and always fall back if
// the body is empty.
async function readErrorMessage(response: Response): Promise<string> {
  const text = await response.text();

  if (!text) return `Request failed (${response.status}).`;

  try {
    const parsed: unknown = JSON.parse(text);
    return typeof parsed === "string" ? parsed : text;
  } catch {
    return text;
  }
}

export function getDevices(apiKey: string): Promise<DeviceSummary[]> {
  return request<DeviceSummary[]>("/devices", apiKey);
}

export function getDevice(apiKey: string, deviceId: string): Promise<DeviceSummary> {
  return request<DeviceSummary>(`/devices/${encodeURIComponent(deviceId)}`, apiKey);
}

export function getDeviceCapabilities(apiKey: string, deviceId: string): Promise<DeviceCapabilities> {
  return request<DeviceCapabilities>(
    `/devices/${encodeURIComponent(deviceId)}/capabilities`,
    apiKey,
  );
}

export function getDeviceEvents(apiKey: string, deviceId: string, take = 50): Promise<DeviceEvent[]> {
  return request<DeviceEvent[]>(
    `/devices/${encodeURIComponent(deviceId)}/events?take=${take}`,
    apiKey,
  );
}

export function getEvents(apiKey: string, take = 50): Promise<DeviceEvent[]> {
  return request<DeviceEvent[]>(`/events?take=${take}`, apiKey);
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

export function getCapabilities(apiKey: string): Promise<CapabilityAdmin[]> {
  return request<CapabilityAdmin[]>("/capabilities-admin", apiKey);
}

export function createCapability(
  apiKey: string,
  capabilityName: string,
  capabilityType: CapabilityType,
): Promise<CapabilityAdmin> {
  return request<CapabilityAdmin>("/capabilities-admin", apiKey, {
    method: "POST",
    body: { capabilityName, capabilityType },
  });
}

export function updateCapability(
  apiKey: string,
  capabilityId: string,
  capabilityName: string,
  capabilityType: CapabilityType,
): Promise<CapabilityAdmin> {
  return request<CapabilityAdmin>(`/capabilities-admin/${encodeURIComponent(capabilityId)}`, apiKey, {
    method: "PUT",
    body: { capabilityName, capabilityType },
  });
}

// Doesn't reuse request<T>() - DELETE returns 204 with no JSON body to parse,
// same reasoning as restartAgent/deployAgent above.
export async function deleteCapability(apiKey: string, capabilityId: string): Promise<void> {
  const response = await fetch(
    `${API_BASE_URL}/capabilities-admin/${encodeURIComponent(capabilityId)}`,
    {
      method: "DELETE",
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

export function getDeviceTypes(apiKey: string): Promise<DeviceTypeAdmin[]> {
  return request<DeviceTypeAdmin[]>("/device-types-admin", apiKey);
}

export function createDeviceType(apiKey: string, deviceTypeName: string): Promise<DeviceTypeAdmin> {
  return request<DeviceTypeAdmin>("/device-types-admin", apiKey, {
    method: "POST",
    body: { deviceTypeName },
  });
}

export function updateDeviceType(
  apiKey: string,
  deviceTypeId: string,
  deviceTypeName: string,
): Promise<DeviceTypeAdmin> {
  return request<DeviceTypeAdmin>(`/device-types-admin/${encodeURIComponent(deviceTypeId)}`, apiKey, {
    method: "PUT",
    body: { deviceTypeName },
  });
}

// Doesn't reuse request<T>() - DELETE returns 204 with no JSON body to parse.
export async function deleteDeviceType(apiKey: string, deviceTypeId: string): Promise<void> {
  const response = await fetch(
    `${API_BASE_URL}/device-types-admin/${encodeURIComponent(deviceTypeId)}`,
    {
      method: "DELETE",
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

export function getAgentRegistry(apiKey: string): Promise<AgentRegistry[]> {
  return request<AgentRegistry[]>("/agents-registry-admin", apiKey);
}

export function createAgentRegistryEntry(
  apiKey: string,
  name: string,
  firmwareVersion: string,
  type: AgentRegistryType,
  capabilityIds: string[],
): Promise<AgentRegistry> {
  return request<AgentRegistry>("/agents-registry-admin", apiKey, {
    method: "POST",
    body: { name, firmwareVersion, type, capabilityIds },
  });
}

export function updateAgentRegistryEntry(
  apiKey: string,
  agentId: string,
  name: string,
  firmwareVersion: string,
  type: AgentRegistryType,
  capabilityIds: string[],
): Promise<AgentRegistry> {
  return request<AgentRegistry>(`/agents-registry-admin/${encodeURIComponent(agentId)}`, apiKey, {
    method: "PUT",
    body: { name, firmwareVersion, type, capabilityIds },
  });
}

// Doesn't reuse request<T>() - DELETE returns 204 with no JSON body to parse.
export async function deleteAgentRegistryEntry(apiKey: string, agentId: string): Promise<void> {
  const response = await fetch(
    `${API_BASE_URL}/agents-registry-admin/${encodeURIComponent(agentId)}`,
    {
      method: "DELETE",
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

export function getDeviceRegistry(apiKey: string): Promise<DeviceRegistry[]> {
  return request<DeviceRegistry[]>("/devices-registry-admin", apiKey);
}

export interface DeviceRegistryFields {
  name: string;
  deviceTypeId: string;
  owningAgentId: string;
  location: string;
  brand: string;
  model: string;
  firmware: string;
  enabled: boolean;
  capabilityIds: string[];
  settings: Record<string, string>;
}

export function createDeviceRegistryEntry(
  apiKey: string,
  fields: DeviceRegistryFields,
): Promise<DeviceRegistry> {
  return request<DeviceRegistry>("/devices-registry-admin", apiKey, {
    method: "POST",
    body: fields,
  });
}

export function updateDeviceRegistryEntry(
  apiKey: string,
  deviceId: string,
  fields: DeviceRegistryFields,
): Promise<DeviceRegistry> {
  return request<DeviceRegistry>(`/devices-registry-admin/${encodeURIComponent(deviceId)}`, apiKey, {
    method: "PUT",
    body: fields,
  });
}

// Doesn't reuse request<T>() - DELETE returns 204 with no JSON body to parse.
export async function deleteDeviceRegistryEntry(apiKey: string, deviceId: string): Promise<void> {
  const response = await fetch(
    `${API_BASE_URL}/devices-registry-admin/${encodeURIComponent(deviceId)}`,
    {
      method: "DELETE",
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
