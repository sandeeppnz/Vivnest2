import { useState } from "react";
import { ApiKeyGate, clearStoredApiKey, loadStoredApiKey } from "./ApiKeyGate";
import { DeviceList } from "./DeviceList";
import { DeviceDetail } from "./DeviceDetail";
import { AgentList } from "./AgentList";
import "./App.css";

type View = "devices" | "agents";

function App() {
  const [apiKey, setApiKey] = useState<string | null>(loadStoredApiKey);
  const [view, setView] = useState<View>("devices");
  const [selectedDeviceId, setSelectedDeviceId] = useState<string | null>(null);

  function handleAuthError() {
    clearStoredApiKey();
    setApiKey(null);
    setSelectedDeviceId(null);
  }

  function selectView(next: View) {
    setView(next);
    setSelectedDeviceId(null);
  }

  if (!apiKey) {
    return <ApiKeyGate onSubmit={setApiKey} />;
  }

  return (
    <div className="app">
      <header>
        <h1>Vivnest</h1>
        <nav className="tabs">
          <button
            className={view === "devices" ? "active" : ""}
            onClick={() => selectView("devices")}
          >
            Devices
          </button>
          <button
            className={view === "agents" ? "active" : ""}
            onClick={() => selectView("agents")}
          >
            Agents
          </button>
        </nav>
      </header>
      <main>
        {view === "devices" ? (
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
