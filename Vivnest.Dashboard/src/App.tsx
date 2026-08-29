import { useEffect, useState } from "react";
import { Redirect, Route, Switch, useLocation, useSearch } from "wouter";
import { ApiKeyGate, clearStoredApiKey, loadStoredApiKey } from "./ApiKeyGate";
import { DeviceList } from "./DeviceList";
import { DeviceDetail } from "./DeviceDetail";
import { AgentList } from "./AgentList";
import { AgentDetail } from "./AgentDetail";
import { Overview } from "./Overview";
import { EventsFeed } from "./EventsFeed";
import { HomeOverview } from "./HomeOverview";
import { SettingsPage } from "./SettingsPage";
import { ApiError, getWhoAmI, type ApiKeyRole, type WhoAmI } from "./api";
import { VivnestLogo } from "./icons";
import { ConfirmDialog } from "./ConfirmDialog";
import { ErrorState } from "./ErrorState";
import { NotificationBell } from "./NotificationBell";
import { ThemeToggle } from "./ThemeToggle";
import { CapabilitiesAdmin } from "./CapabilitiesAdmin";
import { ModelsAdmin } from "./ModelsAdmin";
import { AgentRegistryAdmin } from "./AgentRegistryAdmin";
import { DeviceTypesAdmin } from "./DeviceTypesAdmin";
import { DeviceRegistryAdmin } from "./DeviceRegistryAdmin";
import { ApiKeysAdmin } from "./ApiKeysAdmin";
import { MachinesAdmin } from "./MachinesAdmin";
import { AgentInstallationsAdmin } from "./AgentInstallationsAdmin";
import { AddDeviceWizard } from "./AddDeviceWizard";
import { AgentConfigurationPanel } from "./AgentConfigurationPanel";
import { DeviceConfigurationPanel } from "./DeviceConfigurationPanel";
import { ModeSelect } from "./ModeSelect";
import { clearStoredMode, getStoredMode, setStoredMode, type DashboardMode } from "./mode";
import { ADMIN_LINKS, DEVICES_ONLY_NAV, FULL_NAV, ModeBadge, NavSidebar, NavTabBar } from "./navigation";
import { AlertsPage } from "./AlertsPage";
import { CommandsPage } from "./CommandsPage";
import { SessionPage } from "./SessionPage";
import { SessionProvider } from "./session";
import { listPath, statusFilterFrom, toAdminView, toDetailTab, type AdminView } from "./routes";
import "./App.css";

const ADMIN_TITLES: Record<AdminView, string> = {
  capabilities: "Capabilities",
  models: "Models",
  deviceTypes: "Device Types",
  devices: "Devices",
  agents: "Agents",
  machines: "Machines",
  agentInstallations: "Agent Installations",
  apiKeys: "API Keys",
};

function App() {
  const [apiKey, setApiKey] = useState<string | null>(loadStoredApiKey);
  const [devicesOnly, setDevicesOnly] = useState<boolean | null>(null);
  // The key's server-resolved role (roles-on-keys, 2026-08-29). Falls
  // back to "developer" if the deployed Functions predate the field -
  // matching what the server would enforce for such a deploy anyway.
  const [role, setRole] = useState<ApiKeyRole | null>(null);
  const [site, setSite] = useState<Pick<WhoAmI, "tenantId" | "siteId" | "tenantName" | "siteName"> | null>(null);
  // Non-401 whoami failure (network down, API unreachable). Blocks with
  // a retry instead of silently degrading: the old fallback dropped the
  // session into a devicesOnly view with no tenant/site and NO way to
  // recover short of a reload, because the lookup only re-ran when the
  // key changed - found live when a login raced the API host starting.
  const [whoAmIError, setWhoAmIError] = useState<string | null>(null);
  const [whoAmIRetryNonce, setWhoAmIRetryNonce] = useState(0);
  const [logoutConfirmOpen, setLogoutConfirmOpen] = useState(false);
  // Full-access DEVELOPER keys pick a mode (the 2026-08-07 mockup's
  // landing screen); devicesOnly keys get the trimmed device view by
  // decree, and user-role keys are locked to User Mode - the server 403s
  // their admin/action surface, so offering the selector or the switch
  // would promise something the key can't do. null = not chosen yet on
  // this browser.
  const [mode, setMode] = useState<DashboardMode | null>(getStoredMode);

  const [, navigate] = useLocation();
  const search = useSearch();

  function resetSession() {
    clearStoredApiKey();
    // The mode is per-login.
    clearStoredMode();
    setApiKey(null);
    setDevicesOnly(null);
    setRole(null);
    setSite(null);
    setMode(null);
  }

  function reopenModeSelect() {
    clearStoredMode();
    setMode(null);
    navigate("/");
  }

  useEffect(() => {
    if (!apiKey) return;

    let cancelled = false;

    getWhoAmI(apiKey)
      .then((result) => {
        if (cancelled) return;
        setWhoAmIError(null);
        setDevicesOnly(result.devicesOnly);
        setRole(result.role ?? "developer");
        setSite({
          tenantId: result.tenantId,
          siteId: result.siteId,
          tenantName: result.tenantName,
          siteName: result.siteName,
        });
      })
      .catch((err) => {
        if (cancelled) return;

        if (err instanceof ApiError && err.status === 401) {
          resetSession();
          return;
        }

        setWhoAmIError(err instanceof Error ? err.message : "Could not reach the API.");
      });

    return () => {
      cancelled = true;
    };
  }, [apiKey, whoAmIRetryNonce]);

  const selectDevice = (deviceId: string) => navigate(`/devices/${encodeURIComponent(deviceId)}`);
  const selectAgent = (agentId: string) => navigate(`/agents/${encodeURIComponent(agentId)}`);
  const goToDevices = (statusFilter: string | null) => navigate(listPath("/devices", statusFilter));
  const goToAgents = (statusFilter: string | null) => navigate(listPath("/agents", statusFilter));

  if (!apiKey) {
    return <ApiKeyGate onSubmit={setApiKey} />;
  }

  if (whoAmIError) {
    return (
      <div className="app">
        <ErrorState
          message={whoAmIError}
          onRetry={() => {
            setWhoAmIError(null);
            setWhoAmIRetryNonce((n) => n + 1);
          }}
        />
      </div>
    );
  }

  if (devicesOnly === null) {
    return (
      <div className="app">
        <p>Loading...</p>
      </div>
    );
  }

  // Only developer-role keys get the choice; a user-role key IS User
  // Mode, no selector, no PIN theater.
  const canDevelop = !devicesOnly && role !== "user";

  if (canDevelop && mode === null) {
    return <ModeSelect onSelected={setMode} />;
  }

  // User and Developer share the SAME nav and routes - the modes differ
  // only in what Settings exposes (Admin + Debug are Developer's) and
  // in the admin/debug routes redirecting home for User. The trimmed
  // five-tab experience below belongs to the devicesOnly KEY, not to a
  // mode - and since roles-on-keys, the User/Developer line is the
  // key's ROLE first, the chosen mode second: a user-role key can never
  // reach developer even with a stale "developer" stored client-side.
  const developer = canDevelop && mode === "developer";

  function adminScreen(view: AdminView) {
    const body =
      view === "capabilities" ? <CapabilitiesAdmin />
      : view === "models" ? <ModelsAdmin />
      : view === "deviceTypes" ? <DeviceTypesAdmin />
      : view === "devices" ? <DeviceRegistryAdmin />
      : view === "agents" ? <AgentRegistryAdmin />
      : view === "machines" ? <MachinesAdmin />
      : view === "agentInstallations" ? <AgentInstallationsAdmin />
      : <ApiKeysAdmin />;

    return (
      <>
        {/* Back to Settings, where the Admin list lives - this went to "/"
            from before the admin links moved into Settings. */}
        <button type="button" className="back-button admin-back" onClick={() => navigate("/settings")}>
          &larr; Settings
        </button>
        <h3 className="section-heading">{ADMIN_TITLES[view]}</h3>
        {body}
      </>
    );
  }

  // ONE navigation model (see navigation.tsx): the same item list
  // renders as a desktop sidebar and a mobile tab bar, and CSS
  // (.app-shell-sidebar's media query) decides which is visible.
  const navItems = devicesOnly ? DEVICES_ONLY_NAV : FULL_NAV;

  return (
    <SessionProvider apiKey={apiKey} onAuthError={resetSession}>
    <div className="app-shell app-shell-sidebar">
      <NavSidebar items={navItems} mode={devicesOnly ? null : mode} />
      <div className="app app-with-bottom-nav">
      <header className="app-header">
        <div className="app-header-top">
          <div className="app-header-title">
            <span className="app-header-brand">
              <VivnestLogo className="app-header-logo" />
              <h1>Vivnest</h1>
              <ModeBadge mode={devicesOnly ? null : mode} />
            </span>
          </div>
          <div className="app-header-buttons">
            <ThemeToggle />
            <NotificationBell />
          </div>
        </div>
      </header>

      <ConfirmDialog
        open={logoutConfirmOpen}
        message="Log out of Vivnest?"
        confirmLabel="Log out"
        onConfirm={() => {
          setLogoutConfirmOpen(false);
          resetSession();
        }}
        onCancel={() => setLogoutConfirmOpen(false)}
      />
      <main>
        {devicesOnly ? (
          // The devicesOnly experience (plan D4's Home Mode, now keyed to
          // the KEY rather than a chosen mode): the mockup's Home /
          // Devices / History / Alerts / Settings vocabulary - and
          // nothing agent- or admin-shaped, same restriction as ever.
          <Switch>
            <Route path="/">
              <HomeOverview
                onSelectDevice={selectDevice}
                onGoToDevices={goToDevices}
                onGoToHistory={() => navigate("/history")}
              />
            </Route>
            <Route path="/history">
              <EventsFeed onSelectDevice={selectDevice} />
            </Route>
            <Route path="/alerts">
              <AlertsPage onSelectDevice={selectDevice} />
            </Route>
            <Route path="/settings">
              <SettingsPage
                site={site}
                onLogout={() => setLogoutConfirmOpen(true)}
              />
            </Route>
            <Route path="/devices">
              <DeviceList
                devicesOnly={devicesOnly}
                statusFilter={statusFilterFrom(search)}
                onStatusFilterChange={(f) => navigate(listPath("/devices", f), { replace: true })}
                onSelect={selectDevice}
              />
            </Route>
            <Route path="/devices/:deviceId/:tab?">
              {(params) => (
                <DeviceDetail
                  deviceId={decodeURIComponent(params.deviceId)}
                  devicesOnly={devicesOnly}
                  developer={false}
                  activeTab={toDetailTab(params.tab)}
                  onSelectTab={(tab) =>
                    navigate(`/devices/${params.deviceId}${tab === "overview" ? "" : `/${tab}`}`, { replace: true })}
                  onBack={() => navigate("/devices")}
                  onSelectAgent={selectAgent}
                  onSelectDevice={selectDevice}
                />
              )}
            </Route>
            <Route>
              <Redirect to="/" />
            </Route>
          </Switch>
        ) : (
          <Switch>
            <Route path="/">
              <Overview
                onSelectAgent={selectAgent}
                onSelectDevice={selectDevice}
                onGoToAgents={goToAgents}
                onGoToDevices={goToDevices}
                onGoToEvents={() => navigate("/events")}
              />
            </Route>
            <Route path="/devices">
              <DeviceList
                devicesOnly={false}
                statusFilter={statusFilterFrom(search)}
                onStatusFilterChange={(f) => navigate(listPath("/devices", f), { replace: true })}
                onSelect={selectDevice}
              />
            </Route>
            <Route path="/devices/:deviceId/:tab?">
              {(params) => (
                <DeviceDetail
                  deviceId={decodeURIComponent(params.deviceId)}
                  devicesOnly={false}
                  developer={developer}
                  activeTab={toDetailTab(params.tab)}
                  onSelectTab={(tab) =>
                    navigate(`/devices/${params.deviceId}${tab === "overview" ? "" : `/${tab}`}`, { replace: true })}
                  onBack={() => navigate("/devices")}
                  onSelectAgent={selectAgent}
                  onSelectDevice={selectDevice}
                />
              )}
            </Route>
            <Route path="/agents">
              <AgentList
                statusFilter={statusFilterFrom(search)}
                onStatusFilterChange={(f) => navigate(listPath("/agents", f), { replace: true })}
                onSelect={selectAgent}
              />
            </Route>
            <Route path="/agents/:agentId/:tab?">
              {(params) => (
                <AgentDetail
                  agentId={decodeURIComponent(params.agentId)}
                  developer={developer}
                  activeTab={toDetailTab(params.tab)}
                  onSelectTab={(tab) =>
                    navigate(`/agents/${params.agentId}${tab === "overview" ? "" : `/${tab}`}`, { replace: true })}
                  onBack={() => navigate("/agents")}
                  onSelectDevice={selectDevice}
                />
              )}
            </Route>
            <Route path="/events">
              {/* Raw payloads is a debug view - Developer only, same rule
                  as Settings' Debug section. */}
              <EventsFeed onSelectDevice={selectDevice} allowRaw={developer} />
            </Route>
            {/* devicesOnly's nav destination, but reachable by URL for
                everyone - the "routes shared, nav differs" rule. */}
            <Route path="/alerts">
              <AlertsPage onSelectDevice={selectDevice} />
            </Route>
            <Route path="/settings">
              {/* The whole User-vs-Developer difference lives here:
                  Developer's Settings carries Admin + Debug and the mode
                  picker; User's carries the free switch to Developer
                  (developer-role keys only - the mode is a lens, the
                  key's role is the boundary, ADR-118). */}
              <SettingsPage
                site={site}
                onLogout={() => setLogoutConfirmOpen(true)}
                adminItems={developer ? ADMIN_LINKS : undefined}
                onSwitchMode={developer ? reopenModeSelect : undefined}
                onUnlockDeveloper={
                  // Only a developer-role key currently in User Mode gets
                  // the unlock - a user-role key would just collect 403s.
                  developer || !canDevelop
                    ? undefined
                    : () => {
                        setStoredMode("developer");
                        setMode("developer");
                        navigate("/settings");
                      }
                }
              />
            </Route>
            {/* Settings -> Debug destinations - Developer only, same
                child-proofing boundary as the admin routes below, and the
                same back-to-Settings affordance as the admin screens. */}
            <Route path="/commands">
              {developer ? (
                <>
                  <button type="button" className="back-button admin-back" onClick={() => navigate("/settings")}>
                    &larr; Settings
                  </button>
                  <h3 className="section-heading">Commands</h3>
                  <CommandsPage />
                </>
              ) : (
                <Redirect to="/" />
              )}
            </Route>
            <Route path="/session">
              {developer ? (
                <>
                  <button type="button" className="back-button admin-back" onClick={() => navigate("/settings")}>
                    &larr; Settings
                  </button>
                  <h3 className="section-heading">Session</h3>
                  <SessionPage />
                </>
              ) : (
                <Redirect to="/" />
              )}
            </Route>
            {/* Retired Developer Mode URL - the raw view is now a toggle
                on the Events feed. */}
            <Route path="/raw-events">
              <Redirect to="/events" />
            </Route>
            <Route path="/admin/devices/new">
              {developer ? (
                <>
                  <button type="button" className="back-button admin-back" onClick={() => navigate("/admin/devices")}>
                    &larr; Devices
                  </button>
                  <h3 className="section-heading">Add a device</h3>
                  <AddDeviceWizard />
                </>
              ) : (
                <Redirect to="/" />
              )}
            </Route>
            <Route path="/admin/devices/:registryId/config">
              {(params) =>
                developer ? (
                  <>
                    <button type="button" className="back-button admin-back" onClick={() => navigate("/admin/devices")}>
                      &larr; Devices
                    </button>
                    <h3 className="section-heading">Configuration</h3>
                    <DeviceConfigurationPanel registryDeviceId={decodeURIComponent(params.registryId)} />
                  </>
                ) : (
                  <Redirect to="/" />
                )}
            </Route>
            <Route path="/admin/agents/:registryId/config">
              {(params) =>
                developer ? (
                  <>
                    <button type="button" className="back-button admin-back" onClick={() => navigate("/admin/agents")}>
                      &larr; Agents
                    </button>
                    <h3 className="section-heading">Configuration</h3>
                    <AgentConfigurationPanel registryAgentId={decodeURIComponent(params.registryId)} />
                  </>
                ) : (
                  <Redirect to="/" />
                )}
            </Route>
            <Route path="/admin/:screen">
              {(params) => {
                const view = developer ? toAdminView(params.screen) : null;
                return view ? adminScreen(view) : <Redirect to="/" />;
              }}
            </Route>
            <Route>
              <Redirect to="/" />
            </Route>
          </Switch>
        )}
      </main>

      <NavTabBar items={navItems} />
      </div>
    </div>
    </SessionProvider>
  );
}

export default App;
