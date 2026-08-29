import { useState, type FormEvent } from "react";

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

  function handleSubmit(e: FormEvent) {
    e.preventDefault();

    const key = sanitizeKey(value);

    if (!key) return;

    localStorage.setItem(STORAGE_KEY, key);
    onSubmit(key);
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
