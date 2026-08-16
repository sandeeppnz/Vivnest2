import { useEffect, useState } from "react";
import { ApiKeyGate, clearStoredApiKey, loadStoredApiKey } from "./ApiKeyGate";
import { DeviceList } from "./DeviceList";
import { DeviceDetail } from "./DeviceDetail";
import { AgentList } from "./AgentList";
import { AgentDetail } from "./AgentDetail";
import { Overview } from "./Overview";
import { EventsFeed } from "./EventsFeed";
import { BottomTabBar, type View } from "./BottomTabBar";
import { ApiError, getWhoAmI, type WhoAmI } from "./api";
import { LogoutIcon, MenuIcon, VivnestLogo } from "./icons";
import { ConfirmDialog } from "./ConfirmDialog";
import { AdminDrawer } from "./AdminDrawer";
import { CapabilitiesAdmin } from "./CapabilitiesAdmin";
import { AgentRegistryAdmin } from "./AgentRegistryAdmin";
import { DeviceTypesAdmin } from "./DeviceTypesAdmin";
import { DeviceRegistryAdmin } from "./DeviceRegistryAdmin";
import { ApiKeysAdmin } from "./ApiKeysAdmin";
import { MachinesAdmin } from "./MachinesAdmin";
import { AgentInstallationsAdmin } from "./AgentInstallationsAdmin";
import { Sidebar, type AdminView } from "./Sidebar";
import "./App.css";

function App() {
  const [apiKey, setApiKey] = useState<string | null>(loadStoredApiKey);
  const [devicesOnly, setDevicesOnly] = useState<boolean | null>(null);
  const [site, setSite] = useState<Pick<WhoAmI, "tenantId" | "siteId" | "tenantName" | "siteName"> | null>(null);
  const [view, setView] = useState<View>("overview");
  const [selectedDeviceId, setSelectedDeviceId] = useState<string | null>(null);
  const [selectedAgentId, setSelectedAgentId] = useState<string | null>(null);
  const [pendingDeviceFilter, setPendingDeviceFilter] = useState<string | null>(null);
  const [pendingAgentFilter, setPendingAgentFilter] = useState<string | null>(null);
  const [logoutConfirmOpen, setLogoutConfirmOpen] = useState(false);
  const [adminDrawerOpen, setAdminDrawerOpen] = useState(false);
  const [adminView, setAdminView] = useState<AdminView | null>(null);

  function resetSession() {
    clearStoredApiKey();
    setApiKey(null);
    setDevicesOnly(null);
    setSite(null);
    setSelectedDeviceId(null);
    setSelectedAgentId(null);
    setAdminDrawerOpen(false);
    setAdminView(null);
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

  function selectView(next: View) {
    setView(next);
    // Also leaves any open Admin screen - the sidebar's main items (and the
    // bottom tabs) both mean "go to that view", not "keep admin open".
    setAdminView(null);
    setSelectedDeviceId(null);
    setSelectedAgentId(null);
    setPendingDeviceFilter(null);
    setPendingAgentFilter(null);
  }

  function selectDevice(deviceId: string) {
    setView("devices");
    setSelectedDeviceId(deviceId);
    setSelectedAgentId(null);
  }

  function selectAgent(agentId: string) {
    setView("agents");
    setSelectedAgentId(agentId);
    setSelectedDeviceId(null);
  }

  function goToDevices(statusFilter: string | null) {
    setView("devices");
    setSelectedDeviceId(null);
    setSelectedAgentId(null);
    setPendingAgentFilter(null);
    setPendingDeviceFilter(statusFilter);
  }

  function goToAgents(statusFilter: string | null) {
    setView("agents");
    setSelectedAgentId(null);
    setSelectedDeviceId(null);
    setPendingDeviceFilter(null);
    setPendingAgentFilter(statusFilter);
  }

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

  const activeView = devicesOnly ? "devices" : view;

  return (
    <div className={`app-shell${!devicesOnly ? " app-shell-sidebar" : ""}`}>
      {!devicesOnly && (
        <Sidebar
          view={activeView}
          adminView={adminView}
          site={site}
          onSelectView={selectView}
          onSelectAdmin={setAdminView}
          onLogout={() => setLogoutConfirmOpen(true)}
        />
      )}
      <div className={`app${!devicesOnly ? " app-with-bottom-nav" : ""}`}>
      <header className="app-header">
        <div className="app-header-top">
          <button
            type="button"
            className="hamburger-button"
            onClick={() => setAdminDrawerOpen(true)}
            aria-label="Open admin menu"
          >
            <MenuIcon />
          </button>
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
          <button
            type="button"
            className="logout-button"
            onClick={() => setLogoutConfirmOpen(true)}
            aria-label="Log out"
          >
            <LogoutIcon className="logout-icon" />
          </button>
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
        onSelectCapabilities={() => {
          setAdminView("capabilities");
          setAdminDrawerOpen(false);
        }}
        onSelectDeviceTypes={() => {
          setAdminView("deviceTypes");
          setAdminDrawerOpen(false);
        }}
        onSelectDevices={() => {
          setAdminView("devices");
          setAdminDrawerOpen(false);
        }}
        onSelectAgents={() => {
          setAdminView("agents");
          setAdminDrawerOpen(false);
        }}
        onSelectMachines={() => {
          setAdminView("machines");
          setAdminDrawerOpen(false);
        }}
        onSelectAgentInstallations={() => {
          setAdminView("agentInstallations");
          setAdminDrawerOpen(false);
        }}
        onSelectApiKeys={() => {
          setAdminView("apiKeys");
          setAdminDrawerOpen(false);
        }}
      />
      <main>
        {adminView === "capabilities" ? (
          <>
            <button type="button" className="back-button admin-back" onClick={() => setAdminView(null)}>
              &larr; Back
            </button>
            <h3 className="section-heading">Capabilities</h3>
            <CapabilitiesAdmin apiKey={apiKey} onAuthError={resetSession} />
          </>
        ) : adminView === "deviceTypes" ? (
          <>
            <button type="button" className="back-button admin-back" onClick={() => setAdminView(null)}>
              &larr; Back
            </button>
            <h3 className="section-heading">Device Types</h3>
            <DeviceTypesAdmin apiKey={apiKey} onAuthError={resetSession} />
          </>
        ) : adminView === "devices" ? (
          <>
            <button type="button" className="back-button admin-back" onClick={() => setAdminView(null)}>
              &larr; Back
            </button>
            <h3 className="section-heading">Devices</h3>
            <DeviceRegistryAdmin apiKey={apiKey} onAuthError={resetSession} />
          </>
        ) : adminView === "agents" ? (
          <>
            <button type="button" className="back-button admin-back" onClick={() => setAdminView(null)}>
              &larr; Back
            </button>
            <h3 className="section-heading">Agents</h3>
            <AgentRegistryAdmin apiKey={apiKey} onAuthError={resetSession} />
          </>
        ) : adminView === "machines" ? (
          <>
            <button type="button" className="back-button admin-back" onClick={() => setAdminView(null)}>
              &larr; Back
            </button>
            <h3 className="section-heading">Machines</h3>
            <MachinesAdmin apiKey={apiKey} onAuthError={resetSession} />
          </>
        ) : adminView === "agentInstallations" ? (
          <>
            <button type="button" className="back-button admin-back" onClick={() => setAdminView(null)}>
              &larr; Back
            </button>
            <h3 className="section-heading">Agent Installations</h3>
            <AgentInstallationsAdmin apiKey={apiKey} onAuthError={resetSession} />
          </>
        ) : adminView === "apiKeys" ? (
          <>
            <button type="button" className="back-button admin-back" onClick={() => setAdminView(null)}>
              &larr; Back
            </button>
            <h3 className="section-heading">API Keys</h3>
            <ApiKeysAdmin />
          </>
        ) : activeView === "overview" ? (
          <Overview
            apiKey={apiKey}
            onSelectAgent={selectAgent}
            onSelectDevice={selectDevice}
            onGoToAgents={goToAgents}
            onGoToDevices={goToDevices}
            onGoToEvents={() => selectView("events")}
            onAuthError={resetSession}
          />
        ) : activeView === "devices" ? (
          selectedDeviceId ? (
            <DeviceDetail
              apiKey={apiKey}
              deviceId={selectedDeviceId}
              devicesOnly={devicesOnly}
              onBack={() => setSelectedDeviceId(null)}
              onSelectAgent={selectAgent}
              onSelectDevice={setSelectedDeviceId}
              onAuthError={resetSession}
            />
          ) : (
            <DeviceList
              apiKey={apiKey}
              devicesOnly={devicesOnly}
              initialStatusFilter={pendingDeviceFilter}
              onSelect={setSelectedDeviceId}
              onAuthError={resetSession}
            />
          )
        ) : activeView === "events" ? (
          <EventsFeed
            apiKey={apiKey}
            onSelectDevice={selectDevice}
            onAuthError={resetSession}
          />
        ) : selectedAgentId ? (
          <AgentDetail
            apiKey={apiKey}
            agentId={selectedAgentId}
            onBack={() => setSelectedAgentId(null)}
            onSelectDevice={selectDevice}
            onAuthError={resetSession}
          />
        ) : (
          <AgentList
            apiKey={apiKey}
            initialStatusFilter={pendingAgentFilter}
            onSelect={setSelectedAgentId}
            onAuthError={resetSession}
          />
        )}
      </main>

      {!devicesOnly && adminView === null && <BottomTabBar active={activeView} onSelect={selectView} />}
      </div>
    </div>
  );
}

export default App;
