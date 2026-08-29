import { useEffect, useState, type FormEvent } from "react";
import {
  createApiKeyOperator,
  createSiteOperator,
  createTenantOperator,
  getTenantsOperator,
  seedCatalogue,
  type CreatedApiKey,
  type SiteAdmin,
  type TenantAdmin,
} from "./api";
import { CheckIcon, CopyIcon } from "./icons";
import { OperatorKeyGate, clearStoredOperatorKey, loadStoredOperatorKey } from "./OperatorKeyGate";

interface BootstrapSetupProps {
  onComplete: (apiKey: string) => void;
  onBack: () => void;
}

// First-run setup, reachable from the login gate: a fresh deployment
// has no API key and no UI path to mint one (the API Keys admin lives
// BEHIND the tenant login), so bootstrap used to be three curl calls
// with the Azure host key. This sequences the same three operator
// endpoints - create tenant, create site, mint a developer key - as one
// screen with three states, not a wizard: nothing here depends on a
// previous step's answer, so there is nothing to step through.
export function BootstrapSetup({ onComplete, onBack }: BootstrapSetupProps) {
  const [hostKey, setHostKey] = useState<string | null>(loadStoredOperatorKey);
  const [existingTenants, setExistingTenants] = useState<TenantAdmin[] | null>(null);
  const [proceedAnyway, setProceedAnyway] = useState(false);
  const [tenantName, setTenantName] = useState("");
  const [siteName, setSiteName] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Partial progress survives a failed call: retrying after "site
  // creation failed" must reuse the tenant already created, never mint
  // a duplicate.
  const [createdTenant, setCreatedTenant] = useState<TenantAdmin | null>(null);
  const [createdSite, setCreatedSite] = useState<SiteAdmin | null>(null);
  const [createdKey, setCreatedKey] = useState<CreatedApiKey | null>(null);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (hostKey === null) return;

    let cancelled = false;

    getTenantsOperator(hostKey)
      .then((tenants) => {
        if (!cancelled) setExistingTenants(tenants);
      })
      .catch(() => {
        // A dead/typoed host key: back to the operator prompt rather
        // than a form whose submits would all fail.
        if (cancelled) return;
        clearStoredOperatorKey();
        setHostKey(null);
      });

    return () => {
      cancelled = true;
    };
  }, [hostKey]);

  async function handleCreate(e: FormEvent) {
    e.preventDefault();

    const tenant = tenantName.trim();
    const site = siteName.trim();

    if (!tenant || !site || busy || hostKey === null) return;

    setBusy(true);
    setError(null);

    try {
      const tenantResult = createdTenant ?? (await createTenantOperator(hostKey, tenant));
      setCreatedTenant(tenantResult);

      const siteResult =
        createdSite ?? (await createSiteOperator(hostKey, tenantResult.tenantId, site));
      setCreatedSite(siteResult);

      const keyResult = await createApiKeyOperator(
        hostKey,
        tenantResult.tenantId,
        siteResult.siteId,
        `${tenant} developer`,
        false,
        "developer",
      );

      // Seed the capability catalogue with the fresh key (ADR-119) so a
      // new deployment never starts with hand-typed capability names and
      // keys - the trap that broke the 2026-08-29 rebuild. Best-effort:
      // the seed is idempotent and rerunnable from Admin > Capabilities,
      // so a failure here must not block sign-in.
      try {
        await seedCatalogue(keyResult.apiKey);
      } catch {
        // "Seed defaults" in Admin > Capabilities covers the retry.
      }

      setCreatedKey(keyResult);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Setup failed.");
    } finally {
      setBusy(false);
    }
  }

  async function handleCopy() {
    if (!createdKey) return;

    try {
      await navigator.clipboard.writeText(createdKey.apiKey);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard can fail - the key is still visible for manual copy.
    }
  }

  if (hostKey === null) {
    return (
      <>
        <OperatorKeyGate onSubmit={setHostKey} />
        <div className="api-key-gate">
          <button type="button" className="link-button" onClick={onBack}>
            Back to login
          </button>
        </div>
      </>
    );
  }

  if (createdKey) {
    return (
      <div className="api-key-gate">
        <h1>You're set up</h1>
        <p>
          Save this API key now - it will never be shown again. It signs in as{" "}
          {createdKey.name} with the developer role.
        </p>
        <div className="list-toolbar" style={{ justifyContent: "center" }}>
          <code style={{ userSelect: "all", wordBreak: "break-all" }}>{createdKey.apiKey}</code>
          <button type="button" className="icon-button" aria-label="Copy key" onClick={handleCopy}>
            {copied ? <CheckIcon /> : <CopyIcon />}
          </button>
        </div>
        <button
          type="button"
          className="form-dialog-save"
          onClick={() => onComplete(createdKey.apiKey)}
        >
          Enter the dashboard
        </button>
      </div>
    );
  }

  if (existingTenants === null) {
    return (
      <div className="api-key-gate">
        <p>Checking this deployment...</p>
      </div>
    );
  }

  if (existingTenants.length > 0 && !proceedAnyway) {
    return (
      <div className="api-key-gate">
        <h1>Already set up</h1>
        <p>
          This deployment already has {existingTenants.length}{" "}
          {existingTenants.length === 1 ? "tenant" : "tenants"} - if you're joining it, ask for an
          API key instead of creating a new tenant.
        </p>
        <button type="button" className="form-dialog-save" onClick={onBack}>
          Back to login
        </button>
        <button type="button" className="link-button" onClick={() => setProceedAnyway(true)}>
          Set up another tenant anyway
        </button>
      </div>
    );
  }

  return (
    <div className="api-key-gate">
      <h1>Set up Vivnest</h1>
      <p>Name your tenant and first site - a developer API key is created for you.</p>
      <form onSubmit={handleCreate}>
        <input
          value={tenantName}
          onChange={(e) => setTenantName(e.target.value)}
          placeholder="Tenant name (e.g. your household)"
          autoFocus
          disabled={createdTenant !== null}
        />
        <input
          value={siteName}
          onChange={(e) => setSiteName(e.target.value)}
          placeholder="Site name (e.g. the address)"
          disabled={createdSite !== null}
        />
        <button type="submit" disabled={busy}>
          {busy ? "Setting up..." : createdTenant ? "Retry" : "Create"}
        </button>
      </form>
      {error && <p className="form-dialog-error">{error}</p>}
      <button type="button" className="link-button" onClick={onBack}>
        Back to login
      </button>
    </div>
  );
}
