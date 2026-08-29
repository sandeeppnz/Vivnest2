import { useState, type FormEvent } from "react";
import { ApiError, getTenantsOperator } from "./api";

const STORAGE_KEY = "vivnest.operatorKey";

// Same paste guard as ApiKeyGate: invisible Unicode smuggled in by a
// copy-paste makes fetch() reject the header outright.
function sanitizeKey(value: string): string {
  return value.replace(/[^\x21-\x7e]/g, "");
}

// sessionStorage, deliberately not the localStorage the tenant key uses:
// this is the Azure Functions host key - the most privileged credential
// the dashboard ever handles (it lists every tenant and mints/revokes
// keys for all of them). localStorage would keep it on disk indefinitely
// and hand it to any script that ever runs on this origin; sessionStorage
// still survives a reload but dies with the tab. Re-entering it per
// browser session is the acceptable cost of that.
export function loadStoredOperatorKey(): string | null {
  // Older builds stored it in localStorage - purge any copy still there,
  // or upgrading would leave the key on disk forever anyway.
  localStorage.removeItem(STORAGE_KEY);

  return sessionStorage.getItem(STORAGE_KEY);
}

export function clearStoredOperatorKey() {
  sessionStorage.removeItem(STORAGE_KEY);
}

interface OperatorKeyGateProps {
  onSubmit: (hostKey: string) => void;
}

// Mirrors ApiKeyGate.tsx's shape, but for the Azure Functions host key
// (operator tier) rather than the tenant x-api-key - a deliberately
// separate login, stored under its own sessionStorage key (see the
// comment on loadStoredOperatorKey for why not localStorage), never
// mixed with the tenant session. Unlike the tenant key (validated via
// GET /whoami), there's no dedicated "who am I" endpoint for the operator
// tier, so this validates by making a real call (list Tenants) and
// checking whether it 401s.
export function OperatorKeyGate({ onSubmit }: OperatorKeyGateProps) {
  const [value, setValue] = useState("");
  const [checking, setChecking] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();

    const trimmed = sanitizeKey(value);
    if (!trimmed) return;

    setChecking(true);
    setError(null);

    try {
      await getTenantsOperator(trimmed);
      sessionStorage.setItem(STORAGE_KEY, trimmed);
      onSubmit(trimmed);
    } catch (err) {
      setChecking(false);
      setError(err instanceof ApiError && err.status === 401 ? "Invalid operator key." : "Something went wrong.");
    }
  }

  return (
    <div className="api-key-gate">
      <h1>Vivnest Operator</h1>
      <p>Enter the Azure Functions host key to manage Tenants, Sites, and API keys.</p>
      <form onSubmit={handleSubmit}>
        <input
          type="password"
          value={value}
          onChange={(e) => setValue(e.target.value)}
          placeholder="Operator key"
          autoFocus
        />
        <button type="submit" disabled={checking}>
          {checking ? "Checking..." : "Continue"}
        </button>
      </form>
      {error && <p className="form-dialog-error">{error}</p>}
    </div>
  );
}
