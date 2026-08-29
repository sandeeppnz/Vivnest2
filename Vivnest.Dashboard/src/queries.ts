import { useQuery } from "@tanstack/react-query";
import {
  getActiveInstallationByAgent,
  getWhoAmI,
  type AgentCommand,
  type AgentSummary,
  getAgent,
  getAgents,
  getDevices,
  getAgentCapabilities,
  getAgentCommands,
  getAgentMetrics,
  getAgentRegistry,
  getCapabilities,
  getCapabilityDependencies,
  getDevice,
  getDeviceBattery,
  getDeviceCapabilities,
  getDeviceCapabilityAssignments,
  getDeviceCaptureDaySummaries,
  getDeviceEvents,
  getDeviceRegistry,
  getDeviceTypeCapabilities,
  getDeviceTypes,
  getEvents,
  getMachines,
  type AgentInstallation,
  type AgentRegistry,
  type MachineAdmin,
} from "./api";
import { useApiKey } from "./session";

// The tenant-tier query hooks (dashboard-redesign-plan.md D2). One
// place decides refresh behaviour:
//
// - Monitoring data (devices, agents, events - the things a wall
//   display should keep current) refetches every 30 seconds and on
//   focus. When that interval needs to change, it changes here.
// - Everything else refetches on focus/stale (10s staleTime from the
//   session's defaults) - admin registries don't change behind your
//   back the way heartbeats do.
//
// Mutations stay inline in the screens that own them; these hooks are
// the shared read side.

const MONITOR = { refetchInterval: 30_000 } as const;

export function useDevices() {
  const apiKey = useApiKey();
  return useQuery({ queryKey: ["devices"], queryFn: () => getDevices(apiKey), ...MONITOR });
}

export function useAgents(enabled = true) {
  const apiKey = useApiKey();
  return useQuery({ queryKey: ["agents"], queryFn: () => getAgents(apiKey), enabled, ...MONITOR });
}

export function useEvents() {
  const apiKey = useApiKey();
  return useQuery({ queryKey: ["events"], queryFn: () => getEvents(apiKey), ...MONITOR });
}

export function useDevice(deviceId: string) {
  const apiKey = useApiKey();
  return useQuery({
    queryKey: ["device", deviceId],
    queryFn: () => getDevice(apiKey, deviceId),
    ...MONITOR,
  });
}

export function useAgent(agentId: string) {
  const apiKey = useApiKey();
  return useQuery({
    queryKey: ["agent", agentId],
    queryFn: () => getAgent(apiKey, agentId),
    ...MONITOR,
  });
}

export function useAgentMetrics(agentId: string) {
  const apiKey = useApiKey();
  return useQuery({
    queryKey: ["agent-metrics", agentId],
    queryFn: () => getAgentMetrics(apiKey, agentId),
  });
}

export function useAgentCommands(agentId: string) {
  const apiKey = useApiKey();
  return useQuery({
    queryKey: ["agent-commands", agentId],
    queryFn: () => getAgentCommands(apiKey, agentId),
  });
}

export function useDeviceEvents(deviceId: string) {
  const apiKey = useApiKey();
  return useQuery({
    queryKey: ["device-events", deviceId],
    queryFn: () => getDeviceEvents(apiKey, deviceId),
    ...MONITOR,
  });
}

export function useDeviceBattery(deviceId: string, take = 1) {
  const apiKey = useApiKey();
  return useQuery({
    queryKey: ["device-battery", deviceId, take],
    queryFn: () => getDeviceBattery(apiKey, deviceId, take),
  });
}

export function useDeviceCapabilities(deviceId: string) {
  const apiKey = useApiKey();
  return useQuery({
    queryKey: ["device-capabilities", deviceId],
    queryFn: () => getDeviceCapabilities(apiKey, deviceId),
  });
}

export function useCaptureDaySummaries(deviceId: string, days: number) {
  const apiKey = useApiKey();
  return useQuery({
    queryKey: ["capture-days", deviceId, days],
    queryFn: () => getDeviceCaptureDaySummaries(apiKey, deviceId, days),
  });
}

// ---- admin registries ------------------------------------------------------

export function useCapabilityCatalogue() {
  const apiKey = useApiKey();
  return useQuery({ queryKey: ["capability-catalogue"], queryFn: () => getCapabilities(apiKey) });
}

export function useDeviceTypeCatalogue() {
  const apiKey = useApiKey();
  return useQuery({ queryKey: ["device-type-catalogue"], queryFn: () => getDeviceTypes(apiKey) });
}

export function useAgentRegistryList(enabled = true) {
  const apiKey = useApiKey();
  return useQuery({ queryKey: ["agent-registry"], queryFn: () => getAgentRegistry(apiKey), enabled });
}

export function useDeviceRegistryList(enabled = true) {
  const apiKey = useApiKey();
  return useQuery({ queryKey: ["device-registry"], queryFn: () => getDeviceRegistry(apiKey), enabled });
}

export function useMachines() {
  const apiKey = useApiKey();
  return useQuery({ queryKey: ["machines"], queryFn: () => getMachines(apiKey) });
}

export function useCapabilityDependencies(enabled = true) {
  const apiKey = useApiKey();
  return useQuery({
    queryKey: ["capability-dependencies"],
    queryFn: () => getCapabilityDependencies(apiKey),
    enabled,
  });
}

export function useDeviceTypeCapabilities(enabled = true) {
  const apiKey = useApiKey();
  return useQuery({
    queryKey: ["device-type-capabilities"],
    queryFn: () => getDeviceTypeCapabilities(apiKey),
    enabled,
  });
}

export function useAgentCapabilityDeclarations(agentId: string | null) {
  const apiKey = useApiKey();
  return useQuery({
    queryKey: ["agent-capability-declarations", agentId],
    queryFn: () => getAgentCapabilities(apiKey, agentId!),
    enabled: agentId !== null,
  });
}

export function useDeviceCapabilityAssignments(deviceId: string | null) {
  const apiKey = useApiKey();
  return useQuery({
    queryKey: ["device-capability-assignments", deviceId],
    queryFn: () => getDeviceCapabilityAssignments(apiKey, deviceId!),
    enabled: deviceId !== null,
  });
}

export function useWhoAmIRaw() {
  const apiKey = useApiKey();
  return useQuery({ queryKey: ["whoami"], queryFn: () => getWhoAmI(apiKey) });
}

// The cross-agent command debugger (Settings -> Debug): no bulk endpoint, so
// one composite query fans out over the runtime agent ids and merges,
// newest first. Rides the monitoring refresh - a command you just
// issued should show its status marching without a manual reload.
export interface TaggedCommand extends AgentCommand {
  agentName: string;
}

export function useAllAgentCommands(agents: AgentSummary[] | undefined) {
  const apiKey = useApiKey();
  const ids = (agents ?? []).map((a) => a.agentId);

  return useQuery({
    queryKey: ["all-agent-commands", ids],
    queryFn: async (): Promise<TaggedCommand[]> => {
      const perAgent = await Promise.all(
        (agents ?? []).map(async (a) =>
          (await getAgentCommands(apiKey, a.agentId)).map((c) => ({ ...c, agentName: a.name || a.agentId })),
        ),
      );

      return perAgent
        .flat()
        .sort((x, y) => Date.parse(y.createdUtc) - Date.parse(x.createdUtc));
    },
    enabled: ids.length > 0,
    ...MONITOR,
  });
}

// DeviceCapabilitiesModal's eligible-agent filter needs every agent's
// Active declarations - no bulk endpoint, so one composite query does
// the per-agent fan-out, keyed by the agent ids it covers.
export function useAgentCapabilityMap(agentIds: string[], enabled: boolean) {
  const apiKey = useApiKey();
  return useQuery({
    queryKey: ["agent-capability-map", agentIds],
    queryFn: async () => {
      const entries = await Promise.all(
        agentIds.map(async (id) => [id, await getAgentCapabilities(apiKey, id)] as const),
      );

      const map = new Map<string, Set<string>>();
      for (const [agentId, declarations] of entries) {
        map.set(
          agentId,
          new Set(declarations.filter((d) => d.status === "Active").map((d) => d.capabilityId)),
        );
      }
      return map;
    },
    enabled,
  });
}

// AgentInstallationsAdmin's overview has no bulk endpoint - one
// composite query fetches the registry, the machines, and each agent's
// active installation, same O(N) shape the screen always had.
export interface InstallationsOverview {
  agents: AgentRegistry[];
  machines: MachineAdmin[];
  installations: Record<string, AgentInstallation | null>;
}

export function useInstallationsOverview() {
  const apiKey = useApiKey();
  return useQuery({
    queryKey: ["agent-installations-overview"],
    queryFn: async (): Promise<InstallationsOverview> => {
      const [agents, machines] = await Promise.all([
        getAgentRegistry(apiKey),
        getMachines(apiKey),
      ]);

      const entries = await Promise.all(
        agents.map(async (a) => [a.agentId, await getActiveInstallationByAgent(apiKey, a.agentId)] as const),
      );

      return { agents, machines, installations: Object.fromEntries(entries) };
    },
  });
}
