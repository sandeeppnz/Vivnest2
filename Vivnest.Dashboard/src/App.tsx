import { useEffect, useState } from "react";
import { Redirect, Route, Switch, useLocation, useSearch } from "wouter";
import { ApiKeyGate, clearStoredApiKey, loadStoredApiKey } from "./ApiKeyGate";
import { DeviceList } from "./DeviceList";
import { DeviceDetail } from "./DeviceDetail";
import { AgentList } from "./AgentList";
import { AgentDetail } from "./AgentDetail";
import { Overview } from "./Overview";
import { EventsFeed } from "./EventsFeed";
import { BottomTabBar, type View } from "./BottomTabBar";
import { HomeBottomTabBar, type HomeView } from "./HomeBottomTabBar";
import { HomeOverview } from "./HomeOverview";
import { SettingsPage } from "./SettingsPage";
import { ApiError, getWhoAmI, type WhoAmI } from "./api";
import { LogoutIcon, MenuIcon, VivnestLogo } from "./icons";
import { ConfirmDialog } from "./ConfirmDialog";
import { AdminDrawer } from "./AdminDrawer";
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
import { SessionProvider } from "./session";
import { Sidebar, type AdminView } from "./Sidebar";
import { SLUG_BY_ADMIN, listPath, statusFilterFrom, toAdminView, toDetailTab } from "./routes";
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
  const [adminDrawerOpen, setAdminDrawerOpen] = useState(false);
  // Full-access keys pick a mode (the 2026-08-07 mockup's landing
  // screen); devicesOnly keys are locked to Home Mode server-side and
  // never see the selector. null = not chosen yet on this browser.
  const [mode, setMode] = useState<DashboardMode | null>(getStoredMode);

  const [location, navigate] = useLocation();
  const search = useSearch();

  function resetSession() {
    clearStoredApiKey();
    // The mode is per-login; the Home PIN survives on this browser.
    clearStoredMode();
    setApiKey(null);
    setDevicesOnly(null);
    setSite(null);
    setMode(null);
    setAdminDrawerOpen(false);
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

  // The nav components predate the router and take an active view plus
  // callbacks; both are derived from / mapped onto the URL here rather
  // than rewriting them.
  const activeView: View = location.startsWith("/devices")
    ? "devices"
    : location.startsWith("/agents")
      ? "agents"
      : location.startsWith("/events")
        ? "events"
        : "overview";

  const adminView: AdminView | null = location.startsWith("/admin/")
    ? toAdminView(location.split("/")[2])
    : null;

  // Home Mode's tab vocabulary (plan D4) - a devicesOnly session
  // navigates Home / Devices / History / Settings.
  const homeView: HomeView = location.startsWith("/devices")
    ? "devices"
    : location.startsWith("/history")
      ? "history"
      : location.startsWith("/settings")
        ? "settings"
        : "home";

  function selectHomeView(next: HomeView) {
    navigate(next === "home" ? "/" : `/${next}`);
  }

  function selectView(next: View) {
    navigate(next === "overview" ? "/" : `/${next}`);
  }

  function selectAdmin(next: AdminView) {
    navigate(`/admin/${SLUG_BY_ADMIN[next]}`);
  }

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

  return (
    <SessionProvider apiKey={apiKey} onAuthError={resetSession}>
    <div className={`app-shell${!homeMode ? " app-shell-sidebar" : ""}`}>
      {!homeMode && (
        <Sidebar
          view={activeView}
          adminView={adminView}
          site={site}
          onSelectView={selectView}
          onSelectAdmin={selectAdmin}
          onSwitchMode={reopenModeSelect}
          onLogout={() => setLogoutConfirmOpen(true)}
        />
      )}
      <div className="app app-with-bottom-nav">
      <header className="app-header">
        <div className="app-header-top">
          {!homeMode && (
            <button
              type="button"
              className="hamburger-button"
              onClick={() => setAdminDrawerOpen(true)}
              aria-label="Open admin menu"
            >
              <MenuIcon />
            </button>
          )}
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
          {!homeMode && (
            <button
              type="button"
              className="logout-button"
              onClick={() => setLogoutConfirmOpen(true)}
              aria-label="Log out"
            >
              <LogoutIcon className="logout-icon" />
            </button>
          )}
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
      <AdminDrawer
        open={adminDrawerOpen}
        onClose={() => setAdminDrawerOpen(false)}
        onSelectCapabilities={() => { selectAdmin("capabilities"); setAdminDrawerOpen(false); }}
        onSelectDeviceTypes={() => { selectAdmin("deviceTypes"); setAdminDrawerOpen(false); }}
        onSelectDevices={() => { selectAdmin("devices"); setAdminDrawerOpen(false); }}
        onSelectAgents={() => { selectAdmin("agents"); setAdminDrawerOpen(false); }}
        onSelectMachines={() => { selectAdmin("machines"); setAdminDrawerOpen(false); }}
        onSelectAgentInstallations={() => { selectAdmin("agentInstallations"); setAdminDrawerOpen(false); }}
        onSelectApiKeys={() => { selectAdmin("apiKeys"); setAdminDrawerOpen(false); }}
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
              <EventsFeed onSelectDevice={selectDevice} />
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

      {homeMode ? (
        <HomeBottomTabBar active={homeView} onSelect={selectHomeView} />
      ) : (
        adminView === null && <BottomTabBar active={activeView} onSelect={selectView} />
      )}
      </div>
    </div>
    </SessionProvider>
  );
}

export default App;
