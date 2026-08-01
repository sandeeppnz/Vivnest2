import { useEffect, useState } from "react";
import { ApiKeyGate, clearStoredApiKey, loadStoredApiKey } from "./ApiKeyGate";
import { DeviceList } from "./DeviceList";
import { DeviceDetail } from "./DeviceDetail";
import { AgentList } from "./AgentList";
import { ApiError, getWhoAmI } from "./api";
import "./App.css";

type View = "devices" | "agents";

function App() {
  const [apiKey, setApiKey] = useState<string | null>(loadStoredApiKey);
  const [devicesOnly, setDevicesOnly] = useState<boolean | null>(null);
  const [view, setView] = useState<View>("devices");
  const [selectedDeviceId, setSelectedDeviceId] = useState<string | null>(null);

  function handleAuthError() {
    clearStoredApiKey();
    setApiKey(null);
    setDevicesOnly(null);
    setSelectedDeviceId(null);
  }

  useEffect(() => {
    if (!apiKey) return;

    let cancelled = false;

    getWhoAmI(apiKey)
      .then((result) => {
        if (!cancelled) setDevicesOnly(result.devicesOnly);
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
      <header>
        <h1>Vivnest</h1>
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
              onBack={() => setSelectedDeviceId(null)}
              onAuthError={handleAuthError}
            />
          ) : (
            <DeviceList apiKey={apiKey} onSelect={setSelectedDeviceId} onAuthError={handleAuthError} />
          )
        ) : (
          <AgentList apiKey={apiKey} onAuthError={handleAuthError} />
        )}
      </main>
    </div>
  );
}

export default App;
