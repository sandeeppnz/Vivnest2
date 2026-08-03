import { useEffect, useState } from "react";
import { ApiKeyGate, clearStoredApiKey, loadStoredApiKey } from "./ApiKeyGate";
import { DeviceList } from "./DeviceList";
import { DeviceDetail } from "./DeviceDetail";
import { AgentList } from "./AgentList";
import { AgentDetail } from "./AgentDetail";
import { ApiError, getWhoAmI, type WhoAmI } from "./api";
import "./App.css";

type View = "devices" | "agents";

function App() {
  const [apiKey, setApiKey] = useState<string | null>(loadStoredApiKey);
  const [devicesOnly, setDevicesOnly] = useState<boolean | null>(null);
  const [site, setSite] = useState<Pick<WhoAmI, "tenantId" | "siteId"> | null>(null);
  const [view, setView] = useState<View>("devices");
  const [selectedDeviceId, setSelectedDeviceId] = useState<string | null>(null);
  const [selectedAgentId, setSelectedAgentId] = useState<string | null>(null);

  function handleAuthError() {
    clearStoredApiKey();
    setApiKey(null);
    setDevicesOnly(null);
    setSite(null);
    setSelectedDeviceId(null);
    setSelectedAgentId(null);
  }

  useEffect(() => {
    if (!apiKey) return;

    let cancelled = false;

    getWhoAmI(apiKey)
      .then((result) => {
        if (cancelled) return;
        setDevicesOnly(result.devicesOnly);
        setSite({ tenantId: result.tenantId, siteId: result.siteId });
      })
      .catch((err) => {
        if (cancelled) return;

        if (err instanceof ApiError && err.status === 401) {
          handleAuthError();
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
    setSelectedDeviceId(null);
    setSelectedAgentId(null);
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
    <div className="app">
      <header className="app-header">
        <div className="app-header-title">
          <h1>Vivnest</h1>
          {site && (
            <span className="app-header-site">
              {site.tenantId} / {site.siteId}
            </span>
          )}
        </div>
        {!devicesOnly && (
          <nav className="tabs">
            <button
              className={activeView === "devices" ? "active" : ""}
              onClick={() => selectView("devices")}
            >
              Devices
            </button>
            <button
              className={activeView === "agents" ? "active" : ""}
              onClick={() => selectView("agents")}
            >
              Agents
            </button>
          </nav>
        )}
      </header>
      <main>
        {activeView === "devices" ? (
          selectedDeviceId ? (
            <DeviceDetail
              apiKey={apiKey}
              deviceId={selectedDeviceId}
              devicesOnly={devicesOnly}
              onBack={() => setSelectedDeviceId(null)}
              onSelectAgent={selectAgent}
              onAuthError={handleAuthError}
            />
          ) : (
            <DeviceList apiKey={apiKey} onSelect={setSelectedDeviceId} onAuthError={handleAuthError} />
          )
        ) : selectedAgentId ? (
          <AgentDetail
            apiKey={apiKey}
            agentId={selectedAgentId}
            onBack={() => setSelectedAgentId(null)}
            onSelectDevice={selectDevice}
            onAuthError={handleAuthError}
          />
        ) : (
          <AgentList apiKey={apiKey} onSelect={setSelectedAgentId} onAuthError={handleAuthError} />
        )}
      </main>
    </div>
  );
}

export default App;
