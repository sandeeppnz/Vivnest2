import { useEffect, useState } from "react";
import {
  ApiError,
  createApiKeyOperator,
  getApiKeysOperator,
  getSitesOperator,
  getTenantsOperator,
  revokeApiKeyOperator,
  type ApiKeySummary,
  type CreatedApiKey,
  type SiteAdmin,
  type TenantAdmin,
} from "./api";
import { ConfirmDialog } from "./ConfirmDialog";
import { OperatorKeyGate, clearStoredOperatorKey, loadStoredOperatorKey } from "./OperatorKeyGate";
import { CheckIcon, CopyIcon, TrashIcon } from "./icons";

// Operator-tier screen (decision-log.md ADR-054) - owns its own auth
// (OperatorKeyGate), not the tenant apiKey/onAuthError prop pair every
// other Admin screen takes, since this manages Tenants/Sites/API keys
// themselves and needs the operator-tier host key, not a tenant key.
// Tenant -> Site is a dependent dropdown pair (selecting a Tenant loads
// its Sites) - both are needed before an API key can be created or its
// existing keys listed, since GET /apikeys requires both as query params.
export function ApiKeysAdmin() {
  const [hostKey, setHostKey] = useState<string | null>(loadStoredOperatorKey);
  const [tenants, setTenants] = useState<TenantAdmin[] | null>(null);
  const [selectedTenantId, setSelectedTenantId] = useState<string>("");
  const [sites, setSites] = useState<SiteAdmin[] | null>(null);
  const [selectedSiteId, setSelectedSiteId] = useState<string>("");
  const [apiKeys, setApiKeys] = useState<ApiKeySummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [devicesOnly, setDevicesOnly] = useState(false);
  const [creating, setCreating] = useState(false);
  const [createdKey, setCreatedKey] = useState<CreatedApiKey | null>(null);
  const [copied, setCopied] = useState(false);
  const [revokingKey, setRevokingKey] = useState<ApiKeySummary | null>(null);

  function handleError(err: unknown) {
    if (err instanceof ApiError && err.status === 401) {
      clearStoredOperatorKey();
      setHostKey(null);
      return;
    }

    setError(err instanceof Error ? err.message : "Something went wrong.");
  }

  useEffect(() => {
    if (!hostKey) return;

    let cancelled = false;
    setTenants(null);
    setError(null);

    getTenantsOperator(hostKey)
      .then((result) => !cancelled && setTenants(result))
      .catch((err) => !cancelled && handleError(err));

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hostKey]);

  useEffect(() => {
    if (!hostKey || !selectedTenantId) {
      setSites(null);
      return;
    }

    let cancelled = false;
    setSites(null);
    setSelectedSiteId("");

    getSitesOperator(hostKey, selectedTenantId)
      .then((result) => !cancelled && setSites(result))
      .catch((err) => !cancelled && handleError(err));

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hostKey, selectedTenantId]);

  function loadApiKeys() {
    if (!hostKey || !selectedTenantId || !selectedSiteId) return;

    getApiKeysOperator(hostKey, selectedTenantId, selectedSiteId)
      .then(setApiKeys)
      .catch(handleError);
  }

  useEffect(() => {
    if (!hostKey || !selectedTenantId || !selectedSiteId) {
      setApiKeys(null);
      return;
    }

    let cancelled = false;
    setApiKeys(null);

    getApiKeysOperator(hostKey, selectedTenantId, selectedSiteId)
      .then((result) => !cancelled && setApiKeys(result))
      .catch((err) => !cancelled && handleError(err));

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hostKey, selectedTenantId, selectedSiteId]);

  async function handleCreate() {
    if (!hostKey || !selectedTenantId || !selectedSiteId) return;

    setCreating(true);
    setError(null);

    try {
      const result = await createApiKeyOperator(hostKey, selectedTenantId, selectedSiteId, name.trim(), devicesOnly);
      setCreatedKey(result);
      setCopied(false);
      setName("");
      setDevicesOnly(false);
      loadApiKeys();
    } catch (err) {
      handleError(err);
    } finally {
      setCreating(false);
    }
  }

  async function handleRevoke() {
    if (!hostKey || !revokingKey) return;

    try {
      await revokeApiKeyOperator(hostKey, revokingKey.keyId);
      setRevokingKey(null);
      loadApiKeys();
    } catch (err) {
      setRevokingKey(null);
      handleError(err);
    }
  }

  async function handleCopy() {
    if (!createdKey) return;

    try {
      await navigator.clipboard.writeText(createdKey.apiKey);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard API can fail (permissions, insecure context) - the key
      // is still visible in the box for a manual copy.
    }
  }

  function logOut() {
    clearStoredOperatorKey();
    setHostKey(null);
    setTenants(null);
    setSelectedTenantId("");
    setSites(null);
    setSelectedSiteId("");
    setApiKeys(null);
    setCreatedKey(null);
  }

  if (!hostKey) {
    return <OperatorKeyGate onSubmit={setHostKey} />;
  }

  if (error) return <p className="error">{error}</p>;

  return (
    <div>
      <div className="list-toolbar">
        <p className="form-hint" style={{ margin: 0 }}>Signed in as operator.</p>
        <button type="button" className="confirm-dialog-cancel" onClick={logOut}>
          Log out of operator mode
        </button>
      </div>

      {!tenants ? (
        <p>Loading tenants...</p>
      ) : (
        <div className="form-field">
          <label className="form-label" htmlFor="apikey-tenant">Tenant</label>
          <select
            id="apikey-tenant"
            className="form-select"
            value={selectedTenantId}
            onChange={(e) => setSelectedTenantId(e.target.value)}
          >
            <option value="">Select a tenant...</option>
            {tenants.map((t) => (
              <option key={t.tenantId} value={t.tenantId}>
                {t.name}{t.status !== "Active" ? ` (${t.status})` : ""}
              </option>
            ))}
          </select>
        </div>
      )}

      {selectedTenantId && (
        <div className="form-field">
          <label className="form-label" htmlFor="apikey-site">Site</label>
          {!sites ? (
            <p>Loading sites...</p>
          ) : sites.length === 0 ? (
            <p className="form-hint">This tenant has no sites yet.</p>
          ) : (
            <select
              id="apikey-site"
              className="form-select"
              value={selectedSiteId}
              onChange={(e) => setSelectedSiteId(e.target.value)}
            >
              <option value="">Select a site...</option>
              {sites.map((s) => (
                <option key={s.siteId} value={s.siteId}>
                  {s.name}{s.status !== "Active" ? ` (${s.status})` : ""}
                </option>
              ))}
            </select>
          )}
        </div>
      )}

      {selectedTenantId && selectedSiteId && (
        <>
          <h3 className="section-heading">Existing keys</h3>
          {!apiKeys ? (
            <p>Loading keys...</p>
          ) : apiKeys.length === 0 ? (
            <p>No API keys yet for this tenant/site.</p>
          ) : (
            <div className="entity-list">
              {apiKeys.map((k) => (
                <div className="entity-row entity-row-static" key={k.keyId}>
                  <div className="entity-row-main">
                    <div>
                      <div className="entity-row-title">{k.name || "(unnamed)"}</div>
                      <div className="entity-row-subtitle" style={{ fontFamily: "monospace" }}>{k.keyId}</div>
                    </div>
                  </div>
                  <div className="entity-row-actions">
                    {k.devicesOnly && <span className="status status-accent">Devices only</span>}
                    <span className={`status ${k.enabled ? "status-online" : "status-offline"}`}>
                      {k.enabled ? "Enabled" : "Revoked"}
                    </span>
                    {k.enabled && (
                      <button
                        type="button"
                        className="icon-button icon-button-danger"
                        aria-label={`Revoke ${k.name || k.keyId}`}
                        onClick={() => setRevokingKey(k)}
                      >
                        <TrashIcon />
                      </button>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}

          <h3 className="section-heading">Create a new key</h3>
          {createdKey ? (
            <div className="form-field">
              <p className="form-hint">
                Save this key now - it will never be shown again.
              </p>
              <div className="list-toolbar">
                <code style={{ userSelect: "all", wordBreak: "break-all" }}>{createdKey.apiKey}</code>
                <button type="button" className="icon-button" aria-label="Copy key" onClick={handleCopy}>
                  {copied ? <CheckIcon /> : <CopyIcon />}
                </button>
              </div>
              <button type="button" className="form-dialog-save" onClick={() => setCreatedKey(null)}>
                Done
              </button>
            </div>
          ) : (
            <>
              <div className="form-field">
                <label className="form-label" htmlFor="apikey-name">Name</label>
                <input
                  id="apikey-name"
                  className="form-input"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="Optional"
                />
              </div>
              <div className="form-field">
                <label className="form-checklist-item">
                  <input
                    type="checkbox"
                    checked={devicesOnly}
                    onChange={(e) => setDevicesOnly(e.target.checked)}
                  />
                  Devices only (hides Agent list/detail for this key)
                </label>
              </div>
              <button
                type="button"
                className="form-dialog-save"
                disabled={creating}
                onClick={handleCreate}
              >
                {creating ? "Creating..." : "Create key"}
              </button>
            </>
          )}
        </>
      )}

      <ConfirmDialog
        open={revokingKey !== null}
        message={`Revoke key "${revokingKey?.name || revokingKey?.keyId}"? This cannot be undone.`}
        confirmLabel="Revoke"
        onConfirm={handleRevoke}
        onCancel={() => setRevokingKey(null)}
      />
    </div>
  );
}
