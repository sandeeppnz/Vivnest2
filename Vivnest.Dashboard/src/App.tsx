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
import { ApiError, getWhoAmI, type WhoAmI } from "./api";
import { VivnestLogo } from "./icons";
import { ConfirmDialog } from "./ConfirmDialog";
import { NotificationBell } from "./NotificationBell";
import { CapabilitiesAdmin } from "./CapabilitiesAdmin";
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
import { ADMIN_LINKS, HOME_NAV, INSTALLER_NAV, NavSidebar, NavTabBar } from "./navigation";
import { AlertsPage } from "./AlertsPage";
import { CommandsPage } from "./CommandsPage";
import { SessionPage } from "./SessionPage";
import { SessionProvider } from "./session";
import { listPath, statusFilterFrom, toAdminView, toDetailTab, type AdminView } from "./routes";
import "./App.css";

const ADMIN_TITLES: Record<AdminView, string> = {
  capabilities: "Capabilities",
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
  const [site, setSite] = useState<Pick<WhoAmI, "tenantId" | "siteId" | "tenantName" | "siteName"> | null>(null);
  const [logoutConfirmOpen, setLogoutConfirmOpen] = useState(false);
  // Full-access keys pick a mode (the 2026-08-07 mockup's landing
  // screen); devicesOnly keys are locked to Home Mode server-side and
  // never see the selector. null = not chosen yet on this browser.
  const [mode, setMode] = useState<DashboardMode | null>(getStoredMode);

  const [, navigate] = useLocation();
  const search = useSearch();

  function resetSession() {
    clearStoredApiKey();
    // The mode is per-login; the Home PIN survives on this browser.
    clearStoredMode();
    setApiKey(null);
    setDevicesOnly(null);
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
        setDevicesOnly(result.devicesOnly);
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

        // Permissions couldn't be determined - default to the more
        // restrictive view rather than risk showing a tab the key can't use.
        setDevicesOnly(true);
      });

    return () => {
      cancelled = true;
    };
  }, [apiKey]);

  const selectDevice = (deviceId: string) => navigate(`/devices/${encodeURIComponent(deviceId)}`);
  const selectAgent = (agentId: string) => navigate(`/agents/${encodeURIComponent(agentId)}`);
  const goToDevices = (statusFilter: string | null) => navigate(listPath("/devices", statusFilter));
  const goToAgents = (statusFilter: string | null) => navigate(listPath("/agents", statusFilter));

  if (!apiKey) {
    return <ApiKeyGate onSubmit={setApiKey} />;
  }

  if (devicesOnly === null) {
    return (
      <div className="app">
        <p>Loading...</p>
      </div>
    );
  }

  if (!devicesOnly && mode === null) {
    return <ModeSelect onSelected={setMode} />;
  }

  // One flag drives every Home-vs-Installer branch below: a devicesOnly
  // key is Home Mode by decree, a full-access key by choice.
  const homeMode = devicesOnly || mode === "home";

  function adminScreen(view: AdminView) {
    const body =
      view === "capabilities" ? <CapabilitiesAdmin />
      : view === "deviceTypes" ? <DeviceTypesAdmin />
      : view === "devices" ? <DeviceRegistryAdmin />
      : view === "agents" ? <AgentRegistryAdmin />
      : view === "machines" ? <MachinesAdmin />
      : view === "agentInstallations" ? <AgentInstallationsAdmin />
      : <ApiKeysAdmin />;

    return (
      <>
        <button type="button" className="back-button admin-back" onClick={() => navigate("/")}>
          &larr; Back
        </button>
        <h3 className="section-heading">{ADMIN_TITLES[view]}</h3>
        {body}
      </>
    );
  }

  // ONE navigation model per mode (see navigation.tsx): the same item
  // list renders as a desktop sidebar and a mobile tab bar, and CSS
  // (.app-shell-sidebar's media query) decides which is visible.
  const navItems = homeMode ? HOME_NAV : INSTALLER_NAV;

  return (
    <SessionProvider apiKey={apiKey} onAuthError={resetSession}>
    <div className="app-shell app-shell-sidebar">
      <NavSidebar items={navItems} site={site} />
      <div className="app app-with-bottom-nav">
      <header className="app-header">
        <div className="app-header-top">
          <div className="app-header-title">
            <span className="app-header-brand">
              <VivnestLogo className="app-header-logo" />
              <h1>Vivnest</h1>
            </span>
            {site && (
              <span className="app-header-site">
                {site.tenantName ?? site.tenantId} / {site.siteName ?? site.siteId}
              </span>
            )}
          </div>
          <NotificationBell />
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
        {homeMode ? (
          // Home Mode (plan D4/D6): a devicesOnly key gets the mockup's
          // Home / Devices / History / Settings vocabulary - and nothing
          // agent- or admin-shaped, same restriction as ever.
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
                onUnlockInstaller={
                  devicesOnly
                    ? undefined
                    : () => {
                        setStoredMode("installer");
                        setMode("installer");
                        navigate("/");
                      }
                }
              />
            </Route>
            <Route path="/devices">
              <DeviceList
                devicesOnly={homeMode}
                statusFilter={statusFilterFrom(search)}
                onStatusFilterChange={(f) => navigate(listPath("/devices", f), { replace: true })}
                onSelect={selectDevice}
              />
            </Route>
            <Route path="/devices/:deviceId/:tab?">
              {(params) => (
                <DeviceDetail
                  deviceId={decodeURIComponent(params.deviceId)}
                  devicesOnly={homeMode}
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
                devicesOnly={homeMode}
                statusFilter={statusFilterFrom(search)}
                onStatusFilterChange={(f) => navigate(listPath("/devices", f), { replace: true })}
                onSelect={selectDevice}
              />
            </Route>
            <Route path="/devices/:deviceId/:tab?">
              {(params) => (
                <DeviceDetail
                  deviceId={decodeURIComponent(params.deviceId)}
                  devicesOnly={homeMode}
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
                  activeTab={toDetailTab(params.tab)}
                  onSelectTab={(tab) =>
                    navigate(`/agents/${params.agentId}${tab === "overview" ? "" : `/${tab}`}`, { replace: true })}
                  onBack={() => navigate("/agents")}
                  onSelectDevice={selectDevice}
                />
              )}
            </Route>
            <Route path="/events">
              <EventsFeed onSelectDevice={selectDevice} allowRaw />
            </Route>
            {/* Home Mode's nav destination, but reachable by URL in every
                mode - the "routes shared, nav vocabulary differs" rule. */}
            <Route path="/alerts">
              <AlertsPage onSelectDevice={selectDevice} />
            </Route>
            <Route path="/settings">
              <SettingsPage
                site={site}
                onLogout={() => setLogoutConfirmOpen(true)}
                adminItems={ADMIN_LINKS}
                onSwitchMode={reopenModeSelect}
              />
            </Route>
            {/* Settings -> Debug destinations (ex-Developer Mode). */}
            <Route path="/commands">
              <CommandsPage />
            </Route>
            <Route path="/session">
              <SessionPage />
            </Route>
            {/* Retired Developer Mode URL - the raw view is now a toggle
                on the Events feed. */}
            <Route path="/raw-events">
              <Redirect to="/events" />
            </Route>
            <Route path="/admin/devices/new">
              <button type="button" className="back-button admin-back" onClick={() => navigate("/admin/devices")}>
                &larr; Devices
              </button>
              <h3 className="section-heading">Add a device</h3>
              <AddDeviceWizard />
            </Route>
            <Route path="/admin/devices/:registryId/config">
              {(params) => (
                <>
                  <button type="button" className="back-button admin-back" onClick={() => navigate("/admin/devices")}>
                    &larr; Devices
                  </button>
                  <h3 className="section-heading">Configuration</h3>
                  <DeviceConfigurationPanel registryDeviceId={decodeURIComponent(params.registryId)} />
                </>
              )}
            </Route>
            <Route path="/admin/agents/:registryId/config">
              {(params) => (
                <>
                  <button type="button" className="back-button admin-back" onClick={() => navigate("/admin/agents")}>
                    &larr; Agents
                  </button>
                  <h3 className="section-heading">Configuration</h3>
                  <AgentConfigurationPanel registryAgentId={decodeURIComponent(params.registryId)} />
                </>
              )}
            </Route>
            <Route path="/admin/:screen">
              {(params) => {
                const view = toAdminView(params.screen);
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
