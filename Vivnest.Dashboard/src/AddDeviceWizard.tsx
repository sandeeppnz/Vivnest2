import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { useLocation } from "wouter";
import {
  createDeviceRegistryEntry,
  createDeviceType,
  refreshAgentConfiguration,
  type DeviceRegistry,
} from "./api";
import { DeviceCapabilitiesModal } from "./DeviceCapabilitiesModal";
import { DeviceConfigurationPanel } from "./DeviceConfigurationPanel";
import {
  useAgentRegistryList,
  useCapabilityCatalogue,
  useDeviceCapabilityAssignments,
  useDeviceTypeCatalogue,
} from "./queries";
import { useApiKey } from "./session";

// The add-a-device task as ONE flow (dashboard-redesign-plan.md D3)
// instead of five screens and three modals: register the device, assign
// its capabilities, review the projected config, publish, and hand off.
// Every step calls the endpoints that already exist - the wizard is
// sequencing, not new backend surface. The registry browser under
// /admin/devices remains for everything else (edit, retire, relink).
//
// Steps advance only forward: registration CREATES the record, so "back"
// from step 2 would be an edit, which the registry browser owns.

type Step = 1 | 2 | 3 | 4;

const STEP_TITLES: Record<Step, string> = {
  1: "Register the device",
  2: "Assign capabilities",
  3: "Review & publish",
  4: "Done",
};

export function AddDeviceWizard() {
  const apiKey = useApiKey();
  const queryClient = useQueryClient();
  const [, navigate] = useLocation();

  const deviceTypesQuery = useDeviceTypeCatalogue();
  const agentsQuery = useAgentRegistryList();
  const catalogueQuery = useCapabilityCatalogue();

  const [step, setStep] = useState<Step>(1);
  const [created, setCreated] = useState<DeviceRegistry | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Step 1 fields.
  const [name, setName] = useState("");
  const [deviceTypeId, setDeviceTypeId] = useState("");
  const [owningAgentId, setOwningAgentId] = useState("");
  const [location, setLocation] = useState("");
  const [runtimeDeviceId, setRuntimeDeviceId] = useState("");

  // Inline device-type creation - "create type if missing" without
  // leaving the flow.
  const [creatingType, setCreatingType] = useState(false);
  const [newTypeName, setNewTypeName] = useState("");

  // Step 2 hosts the existing capabilities modal over the wizard rather
  // than duplicating its 500-line assignment flow.
  const [capabilitiesOpen, setCapabilitiesOpen] = useState(false);
  const assignmentsQuery = useDeviceCapabilityAssignments(created?.deviceId ?? null);

  const activeAssignments = useMemo(
    () => assignmentsQuery.data?.filter((a) => a.status === "Active") ?? [],
    [assignmentsQuery.data],
  );

  const capabilityNameById = useMemo(() => {
    const map = new Map<string, string>();
    for (const c of catalogueQuery.data ?? []) map.set(c.capabilityId, c.capabilityName);
    return map;
  }, [catalogueQuery.data]);

  const owningAgent = useMemo(
    () => (agentsQuery.data ?? []).find((a) => a.agentId === owningAgentId) ?? null,
    [agentsQuery.data, owningAgentId],
  );

  const createTypeMutation = useMutation({
    mutationFn: () => createDeviceType(apiKey, newTypeName.trim(), ""),
    onSuccess: (result) => {
      setCreatingType(false);
      setNewTypeName("");
      setDeviceTypeId(result.deviceTypeId);
      queryClient.invalidateQueries({ queryKey: ["device-type-catalogue"] });
    },
    onError: (err) => setError(err.message),
  });

  const registerMutation = useMutation({
    mutationFn: () =>
      createDeviceRegistryEntry(apiKey, {
        name: name.trim(),
        deviceTypeId,
        owningAgentId,
        location: location.trim(),
        brand: "",
        model: "",
        firmware: "",
        runtimeDeviceId: runtimeDeviceId.trim(),
        settings: {},
      }),
    onSuccess: (result) => {
      setCreated(result);
      setStep(2);
      queryClient.invalidateQueries({ queryKey: ["device-registry"] });
    },
    onError: (err) => setError(err.message),
  });

  // The hand-off: after publish, the owning agent adopts the new config
  // on its next refresh - offer that refresh right here when the agent
  // link is resolvable.
  const refreshMutation = useMutation({
    mutationFn: () => refreshAgentConfiguration(apiKey, owningAgent!.runtimeAgentId),
    onError: (err) => setError(err.message),
  });

  const canRegister = name.trim().length > 0 && deviceTypeId !== "" && owningAgentId !== "";

  return (
    <>
      <div className="wizard-steps">
        {([1, 2, 3, 4] as Step[]).map((s) => (
          <span key={s} className={`wizard-step${s === step ? " active" : ""}${s < step ? " done" : ""}`}>
            {s}. {STEP_TITLES[s]}
          </span>
        ))}
      </div>

      {error && <p className="form-dialog-error">{error}</p>}

      {step === 1 && (
        <>
          <p className="form-hint">
            Registering reserves the device's identity and declares its planned facts (ADR-048/058) -
            brand/model/settings and the local secrets file are the registry browser's job afterwards.
          </p>
          <div className="form-field">
            <label className="form-label" htmlFor="wizard-name">Name</label>
            <input
              id="wizard-name"
              className="form-input"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Kitchen Camera"
              autoFocus
            />
          </div>
          <div className="form-field">
            <label className="form-label" htmlFor="wizard-type">Device Type</label>
            {deviceTypesQuery.data && deviceTypesQuery.data.length > 0 && !creatingType ? (
              <>
                <select
                  id="wizard-type"
                  className="form-select"
                  value={deviceTypeId}
                  onChange={(e) => setDeviceTypeId(e.target.value)}
                >
                  <option value="">Select a device type...</option>
                  {deviceTypesQuery.data.map((d) => (
                    <option key={d.deviceTypeId} value={d.deviceTypeId}>
                      {d.deviceTypeName}
                    </option>
                  ))}
                </select>
                <button type="button" className="form-kv-add" onClick={() => setCreatingType(true)}>
                  + New type
                </button>
              </>
            ) : (
              <div className="list-toolbar">
                <input
                  className="form-input"
                  value={newTypeName}
                  onChange={(e) => setNewTypeName(e.target.value)}
                  placeholder="Camera"
                />
                <button
                  type="button"
                  className="form-dialog-save"
                  disabled={!newTypeName.trim() || createTypeMutation.isPending}
                  onClick={() => {
                    setError(null);
                    createTypeMutation.mutate();
                  }}
                >
                  {createTypeMutation.isPending ? "Creating..." : "Create type"}
                </button>
                {deviceTypesQuery.data && deviceTypesQuery.data.length > 0 && (
                  <button type="button" className="confirm-dialog-cancel" onClick={() => setCreatingType(false)}>
                    Cancel
                  </button>
                )}
              </div>
            )}
          </div>
          <div className="form-field">
            <label className="form-label" htmlFor="wizard-agent">Owning Agent</label>
            <select
              id="wizard-agent"
              className="form-select"
              value={owningAgentId}
              onChange={(e) => setOwningAgentId(e.target.value)}
            >
              <option value="">Select an agent...</option>
              {(agentsQuery.data ?? []).map((a) => (
                <option key={a.agentId} value={a.agentId}>
                  {a.name}
                </option>
              ))}
            </select>
          </div>
          <div className="form-field">
            <label className="form-label" htmlFor="wizard-location">Location</label>
            <input
              id="wizard-location"
              className="form-input"
              value={location}
              onChange={(e) => setLocation(e.target.value)}
              placeholder="Kitchen"
            />
          </div>
          <div className="form-field">
            <label className="form-label" htmlFor="wizard-runtime-id">Runtime Device Id</label>
            <input
              id="wizard-runtime-id"
              className="form-input"
              value={runtimeDeviceId}
              onChange={(e) => setRuntimeDeviceId(e.target.value)}
              placeholder="Optional - the real DeviceId from the device-config/*.json file"
            />
            <p className="form-hint">
              Without this link the review step will show a warning and publishing stays blocked -
              it can also be set later via Edit in the registry browser.
            </p>
          </div>
          <div className="confirm-dialog-actions">
            <button type="button" className="confirm-dialog-cancel" onClick={() => navigate("/admin/devices")}>
              Cancel
            </button>
            <button
              type="button"
              className="form-dialog-save"
              disabled={!canRegister || registerMutation.isPending}
              onClick={() => {
                setError(null);
                registerMutation.mutate();
              }}
            >
              {registerMutation.isPending ? "Registering..." : "Register & continue"}
            </button>
          </div>
        </>
      )}

      {step === 2 && created && (
        <>
          <p className="form-hint">
            What should {created.name} be able to do, and which agent executes it? Compatibility,
            dependencies and per-capability settings are all enforced in the assignment dialog.
          </p>

          {activeAssignments.length === 0 ? (
            <p>No capabilities assigned yet.</p>
          ) : (
            <ul className="event-list">
              {activeAssignments.map((a) => (
                <li key={a.deviceCapabilityId}>
                  <span className="event-type">{capabilityNameById.get(a.capabilityId) ?? a.capabilityId}</span>
                  <span className={`status ${a.enabled ? "status-online" : "status-offline"}`}>
                    {a.enabled ? "Enabled" : "Disabled"}
                  </span>
                </li>
              ))}
            </ul>
          )}

          <div className="confirm-dialog-actions">
            <button type="button" className="confirm-dialog-cancel" onClick={() => setCapabilitiesOpen(true)}>
              {activeAssignments.length === 0 ? "Assign capabilities" : "Manage capabilities"}
            </button>
            <button type="button" className="form-dialog-save" onClick={() => setStep(3)}>
              Continue to review
            </button>
          </div>

          <DeviceCapabilitiesModal
            open={capabilitiesOpen}
            device={created}
            capabilities={catalogueQuery.data ?? []}
            agents={agentsQuery.data ?? []}
            onClose={() => setCapabilitiesOpen(false)}
          />
        </>
      )}

      {step === 3 && created && (
        <>
          <DeviceConfigurationPanel registryDeviceId={created.deviceId} />
          <div className="confirm-dialog-actions">
            <button type="button" className="confirm-dialog-cancel" onClick={() => setStep(2)}>
              Back to capabilities
            </button>
            <button type="button" className="form-dialog-save" onClick={() => setStep(4)}>
              Finish
            </button>
          </div>
        </>
      )}

      {step === 4 && created && (
        <>
          <p>
            <strong>{created.name}</strong> is registered
            {activeAssignments.length > 0 && ` with ${activeAssignments.length} capabilit${activeAssignments.length === 1 ? "y" : "ies"}`}.
          </p>
          {owningAgent?.runtimeAgentId ? (
            <>
              <p className="form-hint">
                If you published, the owning agent adopts the new configuration on its next refresh.
              </p>
              <div className="confirm-dialog-actions" style={{ justifyContent: "flex-start" }}>
                <button
                  type="button"
                  className="confirm-dialog-cancel"
                  disabled={refreshMutation.isPending}
                  onClick={() => {
                    setError(null);
                    refreshMutation.mutate();
                  }}
                >
                  {refreshMutation.isSuccess
                    ? "Refresh requested"
                    : refreshMutation.isPending
                      ? "Requesting..."
                      : `Refresh ${owningAgent.name}'s configuration`}
                </button>
              </div>
            </>
          ) : (
            <p className="form-hint">
              The owning agent has no runtime link yet, so it can't be refreshed from here - link it
              in Admin &rarr; Agents.
            </p>
          )}
          <div className="confirm-dialog-actions">
            <button type="button" className="confirm-dialog-cancel" onClick={() => navigate("/admin/devices")}>
              Registry browser
            </button>
            {created.runtimeDeviceId ? (
              <button
                type="button"
                className="form-dialog-save"
                onClick={() => navigate(`/devices/${encodeURIComponent(created.runtimeDeviceId)}`)}
              >
                Open device page
              </button>
            ) : (
              <button type="button" className="form-dialog-save" onClick={() => navigate("/admin/devices/new")}>
                Add another
              </button>
            )}
          </div>
        </>
      )}
    </>
  );
}
