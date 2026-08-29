import { useState, type FormEvent } from "react";
import { useSearch } from "wouter";
import { BootstrapSetup } from "./BootstrapSetup";

const STORAGE_KEY = "vivnest.apiKey";

// Keys are base64 (printable ASCII). Pasting one out of a chat/doc can
// smuggle in invisible Unicode (zero-width spaces, smart punctuation),
// and fetch() then refuses the whole request with "String contains non
// ISO-8859-1 code point" - found live from a pasted key. Strip anything
// outside printable ASCII rather than bounce the user to decode that
// error themselves.
function sanitizeKey(value: string): string {
  return value.replace(/[^\x21-\x7e]/g, "");
}

export function loadStoredApiKey(): string | null {
  // Sanitized on read too: a bad key may already be stored from before
  // this guard existed.
  const stored = localStorage.getItem(STORAGE_KEY);
  return stored === null ? null : sanitizeKey(stored) || null;
}

interface ApiKeyGateProps {
  onSubmit: (apiKey: string) => void;
}

export function ApiKeyGate({ onSubmit }: ApiKeyGateProps) {
  const [value, setValue] = useState("");
  const [bootstrapping, setBootstrapping] = useState(false);
  // The first-run setup link only shows when the URL asks (/?setup) -
  // not security (the flow is gated server-side by the Azure host key,
  // which 401s everything without it), just not advertising an
  // operator tier on a public login page. Deployers find the flag in
  // the README/docs.
  const showSetupLink = new URLSearchParams(useSearch()).has("setup");

  function accept(key: string) {
    localStorage.setItem(STORAGE_KEY, key);
    onSubmit(key);
  }

  function handleSubmit(e: FormEvent) {
    e.preventDefault();

    const key = sanitizeKey(value);

    if (!key) return;

    accept(key);
  }

  // First-run path: a fresh deployment has no key to enter and no way
  // to mint one from inside (the API Keys admin is behind this very
  // gate) - BootstrapSetup breaks that loop with the operator key.
  if (bootstrapping) {
    return <BootstrapSetup onComplete={accept} onBack={() => setBootstrapping(false)} />;
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
      {showSetupLink && (
        <button type="button" className="link-button" onClick={() => setBootstrapping(true)}>
          First time? Set up with the operator key
        </button>
      )}
    </div>
  );
}

export function clearStoredApiKey() {
  localStorage.removeItem(STORAGE_KEY);
}
