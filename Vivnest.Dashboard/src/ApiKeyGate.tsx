import { useState, type FormEvent } from "react";

const STORAGE_KEY = "vivnest.apiKey";

export function loadStoredApiKey(): string | null {
  return localStorage.getItem(STORAGE_KEY);
}

interface ApiKeyGateProps {
  onSubmit: (apiKey: string) => void;
}

export function ApiKeyGate({ onSubmit }: ApiKeyGateProps) {
  const [value, setValue] = useState("");

  function handleSubmit(e: FormEvent) {
    e.preventDefault();

    if (!value.trim()) return;

    localStorage.setItem(STORAGE_KEY, value.trim());
    onSubmit(value.trim());
  }

  return (
    <div className="api-key-gate">
      <h1>Vivnest</h1>
      <p>Enter your API key to view devices.</p>
      <form onSubmit={handleSubmit}>
        <input
          type="password"
          value={value}
          onChange={(e) => setValue(e.target.value)}
          placeholder="API key"
          autoFocus
        />
        <button type="submit">Continue</button>
      </form>
    </div>
  );
}

export function clearStoredApiKey() {
  localStorage.removeItem(STORAGE_KEY);
}
