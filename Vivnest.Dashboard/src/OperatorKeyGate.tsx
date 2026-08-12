import { useState, type FormEvent } from "react";
import { ApiError, getTenantsOperator } from "./api";

const STORAGE_KEY = "vivnest.operatorKey";

export function loadStoredOperatorKey(): string | null {
  return localStorage.getItem(STORAGE_KEY);
}

export function clearStoredOperatorKey() {
  localStorage.removeItem(STORAGE_KEY);
}

interface OperatorKeyGateProps {
  onSubmit: (hostKey: string) => void;
}

// Mirrors ApiKeyGate.tsx's shape, but for the Azure Functions host key
// (operator tier) rather than the tenant x-api-key - a deliberately
// separate login, stored under its own localStorage key, never mixed
// with the tenant session. Unlike the tenant key (validated via
// GET /whoami), there's no dedicated "who am I" endpoint for the operator
// tier, so this validates by making a real call (list Tenants) and
// checking whether it 401s.
export function OperatorKeyGate({ onSubmit }: OperatorKeyGateProps) {
  const [value, setValue] = useState("");
  const [checking, setChecking] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();

    const trimmed = value.trim();
    if (!trimmed) return;

    setChecking(true);
    setError(null);

    try {
      await getTenantsOperator(trimmed);
      localStorage.setItem(STORAGE_KEY, trimmed);
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
