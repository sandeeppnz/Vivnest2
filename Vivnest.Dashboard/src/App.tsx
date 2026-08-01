import { useState } from "react";
import { ApiKeyGate, clearStoredApiKey, loadStoredApiKey } from "./ApiKeyGate";
import { DeviceList } from "./DeviceList";
import { DeviceDetail } from "./DeviceDetail";
import "./App.css";

function App() {
  const [apiKey, setApiKey] = useState<string | null>(loadStoredApiKey);
  const [selectedDeviceId, setSelectedDeviceId] = useState<string | null>(null);

  function handleAuthError() {
    clearStoredApiKey();
    setApiKey(null);
    setSelectedDeviceId(null);
  }

  if (!apiKey) {
    return <ApiKeyGate onSubmit={setApiKey} />;
  }

  return (
    <div className="app">
      <header>
        <h1>Vivnest</h1>
      </header>
      <main>
        {selectedDeviceId ? (
          <DeviceDetail
            apiKey={apiKey}
            deviceId={selectedDeviceId}
            onBack={() => setSelectedDeviceId(null)}
            onAuthError={handleAuthError}
          />
        ) : (
          <DeviceList apiKey={apiKey} onSelect={setSelectedDeviceId} onAuthError={handleAuthError} />
        )}
      </main>
    </div>
  );
}

export default App;
