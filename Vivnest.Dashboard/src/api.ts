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
  // Resolved from tblTenants/tblSites at call time (ADR-055's follow-up) -
  // null if the key's Tenant/Site has since been deleted. Prefer these
  // for display; tenantId/siteId are Guids since ADR-055, not readable.
  tenantName: string | null;
  siteName: string | null;
}

// Admin > Capabilities master-list record (decision-log.md ADR-042) -
// deliberately unrelated to the read-only per-device Capability/
// CapabilityService types above (ADR-040/041) - different concept,
// different lifecycle, kept separate on purpose.
export type CapabilityType = "Device" | "Service" | "System";

// decision-log.md ADR-062 (Phase 5) - Retired means "still referenced by
// a CapabilityDependency/DeviceTypeCapability, can't be hard-deleted, but
// no longer meant to be assigned to new devices."
export type CapabilityStatus = "Active" | "Retired";

export type CapabilityConfigurationFieldType = "String" | "Number" | "Boolean";

// One field of CapabilityAdmin.configurationSchema (ADR-062) - what
// configuration a capability needs (e.g. ObjectDetection's
// confidenceThreshold: Number, 0..1, required).
export interface CapabilityConfigurationField {
  name: string;
  type: CapabilityConfigurationFieldType;
  required: boolean;
  minimum: number | null;
  maximum: number | null;
  allowedValues: string[] | null;
  defaultValue: string | null;
}

export interface CapabilityAdmin {
  capabilityId: string;
  capabilityName: string;
  capabilityType: CapabilityType;
  status: CapabilityStatus;
  configurationSchema: CapabilityConfigurationField[];
  configurationSchemaVersion: number;
  defaultConfiguration: Record<string, string>;
}

// Admin > Agents pre-registration record (decision-log.md ADR-043) -
// deliberately unrelated to AgentSummary above, which reflects real, live
// heartbeat data. Registering an agent here just reserves its identity
// for whoever sets up the physical device later.
export type AgentRegistryType = "Low" | "High";

// Also the "Agent" concept from the Machine/Agent/AgentInstallation spec
// (decision-log.md ADR-053) - description/status/createdUtc/updatedUtc
// added directly here rather than a parallel type.
export type AgentRegistryStatus = "Active" | "Inactive";

// No longer carries capabilityIds (decision-log.md ADR-059) - which
// capabilities an Agent declares now lives in AgentCapability, a real
// per-declaration record instead of a flat id list here.
export interface AgentRegistry {
  agentId: string;
  name: string;
  description: string | null;
  status: AgentRegistryStatus;
  firmwareVersion: string;
  type: AgentRegistryType;
  tenantId: string;
  siteId: string;
  createdUtc: string;
  updatedUtc: string;
}

// Admin > Device Types master-list record (decision-log.md ADR-047) -
// deliberately unrelated to the fixed DeviceType the real per-device
// Capabilities tab / Vivnest.Core.Enums.DeviceType uses. Same split as
// CapabilityAdmin vs. the read-only Capability/CapabilityService types.
export type DeviceTypeStatus = "Active" | "Inactive";

export interface DeviceTypeAdmin {
  deviceTypeId: string;
  deviceTypeName: string;
  description: string | null;
  status: DeviceTypeStatus;
  createdUtc: string;
  updatedUtc: string;
}

// Admin > Devices pre-registration record (decision-log.md ADR-048/058) -
// deliberately unrelated to Device above, which reflects real, live
// heartbeat data. Registering a device here just reserves its identity
// and declares planned facts - it does not configure a real device; the
// device-config blob workflow is unchanged. Settings is non-secret
// connection facts only (Host, Username, ...) - never credentials, see
// DeviceRegistryFormModal's own warning text. Status replaces the old
// Enabled bool (ADR-058) - Active/Disabled/Retired, no DELETE route.
export type DeviceRegistryStatus = "Active" | "Disabled" | "Retired";

export interface DeviceRegistry {
  deviceId: string;
  name: string;
  deviceTypeId: string;
  owningAgentId: string;
  location: string;
  brand: string;
  model: string;
  firmware: string;
  status: DeviceRegistryStatus;
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
  configurationSchema: CapabilityConfigurationField[],
  configurationSchemaVersion: number,
  defaultConfiguration: Record<string, string>,
): Promise<CapabilityAdmin> {
  return request<CapabilityAdmin>("/capabilities-admin", apiKey, {
    method: "POST",
    body: { capabilityName, capabilityType, configurationSchema, configurationSchemaVersion, defaultConfiguration },
  });
}

export function updateCapability(
  apiKey: string,
  capabilityId: string,
  capabilityName: string,
  capabilityType: CapabilityType,
  status: CapabilityStatus,
  configurationSchema: CapabilityConfigurationField[],
  configurationSchemaVersion: number,
  defaultConfiguration: Record<string, string>,
): Promise<CapabilityAdmin> {
  return request<CapabilityAdmin>(`/capabilities-admin/${encodeURIComponent(capabilityId)}`, apiKey, {
    method: "PUT",
    body: {
      capabilityName,
      capabilityType,
      status,
      configurationSchema,
      configurationSchemaVersion,
      defaultConfiguration,
    },
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

// Admin > Agent Capability declaration record (decision-log.md ADR-059) -
// "this Agent has the ability to execute this Capability," independent
// of any device. Named distinctly from AgentRegistry above (which used
// to carry a flat capabilityIds list, now removed).
export interface AgentCapability {
  agentCapabilityId: string;
  agentId: string;
  capabilityId: string;
  status: "Active" | "Removed";
  assignedUtc: string;
  removedUtc: string | null;
  updatedUtc: string;
  tenantId: string;
  siteId: string;
}

export function getAgentCapabilities(apiKey: string, agentId: string): Promise<AgentCapability[]> {
  return request<AgentCapability[]>(`/agent-capabilities-admin/by-agent/${encodeURIComponent(agentId)}`, apiKey);
}

export function assignAgentCapability(
  apiKey: string,
  agentId: string,
  capabilityId: string,
): Promise<AgentCapability> {
  return request<AgentCapability>("/agent-capabilities-admin/assign", apiKey, {
    method: "POST",
    body: { agentId, capabilityId },
  });
}

export function unassignAgentCapability(
  apiKey: string,
  agentId: string,
  capabilityId: string,
): Promise<AgentCapability> {
  return request<AgentCapability>("/agent-capabilities-admin/unassign", apiKey, {
    method: "POST",
    body: { agentId, capabilityId },
  });
}

// Admin > Device Capability assignment record (decision-log.md ADR-057/059) -
// deliberately named "Assignment", not "DeviceCapability" - that name is
// already taken by the unrelated, read-only DeviceCapabilities type above
// (the live per-device Capabilities tab, sourced from the MVP config
// blob via DeviceCapabilitiesQueryService, not this admin table).
export interface DeviceCapabilityAssignment {
  deviceCapabilityId: string;
  deviceId: string;
  capabilityId: string;
  executingAgentId: string;
  enabled: boolean;
  settings: Record<string, string>;
  status: "Active" | "Removed";
  assignedUtc: string;
  removedUtc: string | null;
  updatedUtc: string;
  tenantId: string;
  siteId: string;
}

export function getDeviceCapabilityAssignments(
  apiKey: string,
  deviceId: string,
): Promise<DeviceCapabilityAssignment[]> {
  return request<DeviceCapabilityAssignment[]>(
    `/device-capabilities-admin/by-device/${encodeURIComponent(deviceId)}`,
    apiKey,
  );
}

export function assignDeviceCapability(
  apiKey: string,
  deviceId: string,
  capabilityId: string,
  executingAgentId: string,
  enabled: boolean,
  settings: Record<string, string> | null = null,
): Promise<DeviceCapabilityAssignment> {
  return request<DeviceCapabilityAssignment>("/device-capabilities-admin/assign", apiKey, {
    method: "POST",
    body: { deviceId, capabilityId, executingAgentId: executingAgentId || null, enabled, settings },
  });
}

export function updateDeviceCapabilityAssignment(
  apiKey: string,
  deviceCapabilityId: string,
  executingAgentId: string,
  enabled: boolean,
  settings: Record<string, string> | null = null,
): Promise<DeviceCapabilityAssignment> {
  return request<DeviceCapabilityAssignment>(
    `/device-capabilities-admin/${encodeURIComponent(deviceCapabilityId)}`,
    apiKey,
    {
      method: "PUT",
      body: { executingAgentId: executingAgentId || null, enabled, settings },
    },
  );
}

// Admin > Capability dependency graph (decision-log.md ADR-062, Phase 5) -
// "ObjectDetection requires ImageCapture." Global, tiny list - fetched
// whole and filtered client-side, same pattern getCapabilities/getAgents
// already use for the assignment modals.
export interface CapabilityDependency {
  dependencyId: string;
  capabilityId: string;
  dependsOnCapabilityId: string;
  dependencyType: "Required";
}

export function getCapabilityDependencies(apiKey: string): Promise<CapabilityDependency[]> {
  return request<CapabilityDependency[]>("/capability-dependencies-admin", apiKey);
}

export function addCapabilityDependency(
  apiKey: string,
  capabilityId: string,
  dependsOnCapabilityId: string,
): Promise<CapabilityDependency> {
  return request<CapabilityDependency>("/capability-dependencies-admin/add", apiKey, {
    method: "POST",
    body: { capabilityId, dependsOnCapabilityId },
  });
}

// Doesn't reuse request<T>() - DELETE returns 204 with no JSON body to
// parse, same reasoning as deleteCapability.
export async function removeCapabilityDependency(apiKey: string, dependencyId: string): Promise<void> {
  const response = await fetch(
    `${API_BASE_URL}/capability-dependencies-admin/${encodeURIComponent(dependencyId)}`,
    {
      method: "DELETE",
      headers: { "x-api-key": apiKey },
    },
  );

  if (response.status === 401) {
    throw new ApiError(401, "Invalid API key.");
  }

  if (response.status === 404) {
    throw new ApiError(404, "Not found.");
  }

  if (!response.ok) {
    throw new ApiError(response.status, `Request failed (${response.status}).`);
  }
}

// Admin > Capability/DeviceType compatibility (decision-log.md ADR-062,
// Phase 5) - "Camera supports ObjectDetection." Global, tiny list, same
// fetch-whole-and-filter-client-side pattern as CapabilityDependency.
export interface DeviceTypeCapability {
  deviceTypeCapabilityId: string;
  deviceTypeId: string;
  capabilityId: string;
}

export function getDeviceTypeCapabilities(apiKey: string): Promise<DeviceTypeCapability[]> {
  return request<DeviceTypeCapability[]>("/device-type-capabilities-admin", apiKey);
}

export function addDeviceTypeCapability(
  apiKey: string,
  deviceTypeId: string,
  capabilityId: string,
): Promise<DeviceTypeCapability> {
  return request<DeviceTypeCapability>("/device-type-capabilities-admin/add", apiKey, {
    method: "POST",
    body: { deviceTypeId, capabilityId },
  });
}

export async function removeDeviceTypeCapability(apiKey: string, deviceTypeCapabilityId: string): Promise<void> {
  const response = await fetch(
    `${API_BASE_URL}/device-type-capabilities-admin/${encodeURIComponent(deviceTypeCapabilityId)}`,
    {
      method: "DELETE",
      headers: { "x-api-key": apiKey },
    },
  );

  if (response.status === 401) {
    throw new ApiError(401, "Invalid API key.");
  }

  if (response.status === 404) {
    throw new ApiError(404, "Not found.");
  }

  if (!response.ok) {
    throw new ApiError(response.status, `Request failed (${response.status}).`);
  }
}

export function unassignDeviceCapability(
  apiKey: string,
  deviceId: string,
  capabilityId: string,
): Promise<DeviceCapabilityAssignment> {
  return request<DeviceCapabilityAssignment>("/device-capabilities-admin/unassign", apiKey, {
    method: "POST",
    body: { deviceId, capabilityId },
  });
}

export function getDeviceTypes(apiKey: string): Promise<DeviceTypeAdmin[]> {
  return request<DeviceTypeAdmin[]>("/device-types-admin", apiKey);
}

export function createDeviceType(
  apiKey: string,
  deviceTypeName: string,
  description: string,
): Promise<DeviceTypeAdmin> {
  return request<DeviceTypeAdmin>("/device-types-admin", apiKey, {
    method: "POST",
    body: { deviceTypeName, description: description || null },
  });
}

export function updateDeviceType(
  apiKey: string,
  deviceTypeId: string,
  deviceTypeName: string,
  description: string,
  status: DeviceTypeStatus,
): Promise<DeviceTypeAdmin> {
  return request<DeviceTypeAdmin>(`/device-types-admin/${encodeURIComponent(deviceTypeId)}`, apiKey, {
    method: "PUT",
    body: { deviceTypeName, description: description || null, status },
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
  description: string,
  firmwareVersion: string,
  type: AgentRegistryType,
): Promise<AgentRegistry> {
  return request<AgentRegistry>("/agents-registry-admin", apiKey, {
    method: "POST",
    body: { name, description: description || null, firmwareVersion, type },
  });
}

export function updateAgentRegistryEntry(
  apiKey: string,
  agentId: string,
  name: string,
  description: string,
  status: AgentRegistryStatus,
  firmwareVersion: string,
  type: AgentRegistryType,
): Promise<AgentRegistry> {
  return request<AgentRegistry>(`/agents-registry-admin/${encodeURIComponent(agentId)}`, apiKey, {
    method: "PUT",
    body: { name, description: description || null, status, firmwareVersion, type },
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

// ownerAgentId/deviceTypeId are optional server-side filters (ADR-058).
export function getDeviceRegistry(
  apiKey: string,
  ownerAgentId?: string,
  deviceTypeId?: string,
): Promise<DeviceRegistry[]> {
  const params = new URLSearchParams();
  if (ownerAgentId) params.set("ownerAgentId", ownerAgentId);
  if (deviceTypeId) params.set("deviceTypeId", deviceTypeId);
  const query = params.toString();

  return request<DeviceRegistry[]>(`/devices-registry-admin${query ? `?${query}` : ""}`, apiKey);
}

export interface DeviceRegistryFields {
  name: string;
  deviceTypeId: string;
  owningAgentId: string;
  location: string;
  brand: string;
  model: string;
  firmware: string;
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
  status: DeviceRegistryStatus,
): Promise<DeviceRegistry> {
  return request<DeviceRegistry>(`/devices-registry-admin/${encodeURIComponent(deviceId)}`, apiKey, {
    method: "PUT",
    body: { ...fields, status },
  });
}

// --- Operator-tier: Tenants/Sites/API keys ---
//
// These routes are gated by the Azure Functions host key
// (AuthorizationLevel.Function on the backend), not the tenant x-api-key
// every function above uses - a materially different, more privileged
// credential (it can list/create every tenant, and mint/revoke keys for
// any of them). operatorRequest() sends it as x-functions-key, the
// standard Azure Functions header for this tier, instead of x-api-key.
// See OperatorKeyGate.tsx for where this key is collected/stored -
// deliberately a separate login from the tenant one in ApiKeyGate.tsx.

export interface TenantAdmin {
  tenantId: string;
  name: string;
  description: string | null;
  status: "Active" | "Inactive";
  createdUtc: string;
  updatedUtc: string;
}

export interface SiteAdmin {
  tenantId: string;
  siteId: string;
  name: string;
  description: string | null;
  status: "Active" | "Inactive";
  createdUtc: string;
  updatedUtc: string;
}

export interface ApiKeySummary {
  keyId: string;
  name: string | null;
  tenantId: string;
  siteId: string;
  enabled: boolean;
  devicesOnly: boolean;
  createdUtc: string;
}

// The raw apiKey value is only ever present in this one response - it is
// never returned again by any other endpoint (tblApiKeys stores only a
// hash of it). The UI must show/copy it here or it's gone for good.
export interface CreatedApiKey {
  keyId: string;
  apiKey: string;
  tenantId: string;
  siteId: string;
  name: string | null;
  devicesOnly: boolean;
  createdUtc: string;
}

async function operatorRequest<T>(path: string, hostKey: string, options?: RequestOptions): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    method: options?.method,
    headers: {
      "x-functions-key": hostKey,
      ...(options?.body !== undefined ? { "Content-Type": "application/json" } : {}),
    },
    body: options?.body !== undefined ? JSON.stringify(options.body) : undefined,
  });

  if (response.status === 401) {
    throw new ApiError(401, "Invalid operator key.");
  }

  if (response.status === 404) {
    throw new ApiError(404, "Not found.");
  }

  if (!response.ok) {
    throw new ApiError(response.status, await readErrorMessage(response));
  }

  return (await response.json()) as T;
}

export function getTenantsOperator(hostKey: string): Promise<TenantAdmin[]> {
  return operatorRequest<TenantAdmin[]>("/tenants", hostKey);
}

export function getSitesOperator(hostKey: string, tenantId: string): Promise<SiteAdmin[]> {
  return operatorRequest<SiteAdmin[]>(`/tenants/${encodeURIComponent(tenantId)}/sites`, hostKey);
}

export function getApiKeysOperator(
  hostKey: string,
  tenantId: string,
  siteId: string,
): Promise<ApiKeySummary[]> {
  return operatorRequest<ApiKeySummary[]>(
    `/apikeys?tenantId=${encodeURIComponent(tenantId)}&siteId=${encodeURIComponent(siteId)}`,
    hostKey,
  );
}

export function createApiKeyOperator(
  hostKey: string,
  tenantId: string,
  siteId: string,
  name: string,
  devicesOnly: boolean,
): Promise<CreatedApiKey> {
  return operatorRequest<CreatedApiKey>("/apikeys", hostKey, {
    method: "POST",
    body: { tenantId, siteId, name: name || null, devicesOnly },
  });
}

// Doesn't reuse operatorRequest<T>() - RevokeApiKey returns 200 OkResult()
// with no JSON body, and operatorRequest always calls response.json().
export async function revokeApiKeyOperator(hostKey: string, keyId: string): Promise<void> {
  const response = await fetch(`${API_BASE_URL}/apikeys/${encodeURIComponent(keyId)}/revoke`, {
    method: "POST",
    headers: { "x-functions-key": hostKey },
  });

  if (response.status === 401) {
    throw new ApiError(401, "Invalid operator key.");
  }

  if (response.status === 404) {
    throw new ApiError(404, "Not found.");
  }

  if (!response.ok) {
    throw new ApiError(response.status, await readErrorMessage(response));
  }
}

// --- Admin > Machines (decision-log.md ADR-053) ---
//
// Tenant-tier (x-api-key), like every other admin master list except
// Tenant/Site itself. MachineId is a generated Guid, never accepted on
// create - see MachineDto's own comment.

export type MachineStatus = "Active" | "Offline" | "Retired" | "Decommissioned";

export interface MachineAdmin {
  machineId: string;
  name: string;
  hostname: string | null;
  description: string | null;
  status: MachineStatus;
  operatingSystem: string | null;
  architecture: string | null;
  createdUtc: string;
  updatedUtc: string;
  tenantId: string;
  siteId: string;
}

export interface MachineFields {
  name: string;
  hostname: string | null;
  description: string | null;
  operatingSystem: string | null;
  architecture: string | null;
}

export function getMachines(apiKey: string): Promise<MachineAdmin[]> {
  return request<MachineAdmin[]>("/machines-admin", apiKey);
}

export function createMachine(apiKey: string, fields: MachineFields): Promise<MachineAdmin> {
  return request<MachineAdmin>("/machines-admin", apiKey, {
    method: "POST",
    body: fields,
  });
}

export function updateMachine(
  apiKey: string,
  machineId: string,
  fields: MachineFields & { status: MachineStatus },
): Promise<MachineAdmin> {
  return request<MachineAdmin>(`/machines-admin/${encodeURIComponent(machineId)}`, apiKey, {
    method: "PUT",
    body: fields,
  });
}

// --- Admin > Agent Installations (decision-log.md ADR-053) ---
//
// Lifecycle actions (Install/Move/Uninstall), not CRUD - see
// AgentInstallationsFunction's own comment for why. Purely declarative,
// not wired to the real Vivnest.Agent.Updater deploy pipeline.

export type AgentInstallationStatus = "Active" | "Removed";

export interface AgentInstallation {
  installationId: string;
  agentId: string;
  machineId: string;
  containerId: string | null;
  imageName: string | null;
  imageVersion: string | null;
  status: AgentInstallationStatus;
  installedUtc: string;
  removedUtc: string | null;
  updatedUtc: string;
  tenantId: string;
  siteId: string;
}

// 404 (no active installation) resolves to null rather than throwing -
// "this agent isn't installed anywhere" is an expected, common state, not
// an error.
export async function getActiveInstallationByAgent(
  apiKey: string,
  agentId: string,
): Promise<AgentInstallation | null> {
  try {
    return await request<AgentInstallation>(
      `/agent-installations-admin/active-by-agent/${encodeURIComponent(agentId)}`,
      apiKey,
    );
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) return null;
    throw err;
  }
}

export function getInstallationsByMachine(apiKey: string, machineId: string): Promise<AgentInstallation[]> {
  return request<AgentInstallation[]>(
    `/agent-installations-admin/by-machine/${encodeURIComponent(machineId)}`,
    apiKey,
  );
}

interface InstallFields {
  agentId: string;
  machineId: string;
  containerId?: string | null;
  imageName?: string | null;
  imageVersion?: string | null;
}

export function installAgent(apiKey: string, fields: InstallFields): Promise<AgentInstallation> {
  return request<AgentInstallation>("/agent-installations-admin/install", apiKey, {
    method: "POST",
    body: fields,
  });
}

export function moveAgent(apiKey: string, fields: InstallFields): Promise<AgentInstallation> {
  return request<AgentInstallation>("/agent-installations-admin/move", apiKey, {
    method: "POST",
    body: fields,
  });
}

export function uninstallAgent(apiKey: string, agentId: string): Promise<AgentInstallation> {
  return request<AgentInstallation>("/agent-installations-admin/uninstall", apiKey, {
    method: "POST",
    body: { agentId },
  });
}
