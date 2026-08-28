import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
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
} from "./api";
import { ConfirmDialog } from "./ConfirmDialog";
import { ErrorState } from "./ErrorState";
import { OperatorKeyGate, clearStoredOperatorKey, loadStoredOperatorKey } from "./OperatorKeyGate";
import { CheckIcon, CopyIcon, TrashIcon } from "./icons";

// Every operator-tier query/mutation carries this meta so the session's
// global 401 handler leaves them alone (see session.tsx): a bad operator
// key ends the OPERATOR session, never the tenant one. The 401s are
// handled here instead, by the effect below.
const OPERATOR_META = { operatorTier: true } as const;

function is401(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401;
}

// Operator-tier screen (decision-log.md ADR-054) - owns its own auth
// (OperatorKeyGate), not the tenant session every other Admin screen
// rides, since this manages Tenants/Sites/API keys themselves and needs
// the operator-tier host key. Tenant -> Site is a dependent dropdown pair
// (selecting a Tenant loads its Sites) - both are needed before an API
// key can be created or its existing keys listed, since GET /apikeys
// requires both as query params.
export function ApiKeysAdmin() {
  const queryClient = useQueryClient();

  const [hostKey, setHostKey] = useState<string | null>(loadStoredOperatorKey);
  const [selectedTenantId, setSelectedTenantId] = useState<string>("");
  const [selectedSiteId, setSelectedSiteId] = useState<string>("");
  const [name, setName] = useState("");
  const [devicesOnly, setDevicesOnly] = useState(false);
  const [createdKey, setCreatedKey] = useState<CreatedApiKey | null>(null);
  const [copied, setCopied] = useState(false);
  const [revokingKey, setRevokingKey] = useState<ApiKeySummary | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const tenantsQuery = useQuery({
    queryKey: ["operator", "tenants", hostKey],
    queryFn: () => getTenantsOperator(hostKey!),
    enabled: hostKey !== null,
    meta: OPERATOR_META,
  });

  const sitesQuery = useQuery({
    queryKey: ["operator", "sites", hostKey, selectedTenantId],
    queryFn: () => getSitesOperator(hostKey!, selectedTenantId),
    enabled: hostKey !== null && selectedTenantId !== "",
    meta: OPERATOR_META,
  });

  const keysQuery = useQuery({
    queryKey: ["operator", "keys", hostKey, selectedTenantId, selectedSiteId],
    queryFn: () => getApiKeysOperator(hostKey!, selectedTenantId, selectedSiteId),
    enabled: hostKey !== null && selectedTenantId !== "" && selectedSiteId !== "",
    meta: OPERATOR_META,
  });

  // A 401 from any operator call means the host key is no longer valid -
  // end the operator session and show the gate again.
  const gotUnauthorized = is401(tenantsQuery.error) || is401(sitesQuery.error) || is401(keysQuery.error);

  useEffect(() => {
    if (!gotUnauthorized) return;

    clearStoredOperatorKey();
    setHostKey(null);
  }, [gotUnauthorized]);

  const createMutation = useMutation({
    mutationFn: () => createApiKeyOperator(hostKey!, selectedTenantId, selectedSiteId, name.trim(), devicesOnly),
    meta: OPERATOR_META,
    onSuccess: (result) => {
      setCreatedKey(result);
      setCopied(false);
      setName("");
      setDevicesOnly(false);
      queryClient.invalidateQueries({ queryKey: ["operator", "keys"] });
    },
    onError: (err) => {
      if (is401(err)) {
        clearStoredOperatorKey();
        setHostKey(null);
        return;
      }
      setActionError(err.message);
    },
  });

  const revokeMutation = useMutation({
    mutationFn: (keyId: string) => revokeApiKeyOperator(hostKey!, keyId),
    meta: OPERATOR_META,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["operator", "keys"] }),
    onError: (err) => {
      if (is401(err)) {
        clearStoredOperatorKey();
        setHostKey(null);
        return;
      }
      setActionError(err.message);
    },
    onSettled: () => setRevokingKey(null),
  });

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
    setSelectedTenantId("");
    setSelectedSiteId("");
    setCreatedKey(null);
    queryClient.removeQueries({ queryKey: ["operator"] });
  }

  if (!hostKey) {
    return <OperatorKeyGate onSubmit={setHostKey} />;
  }

  const nonAuthError = [tenantsQuery, sitesQuery, keysQuery].find((q) => q.isError && !is401(q.error));

  if (nonAuthError) {
    return <ErrorState message={nonAuthError.error!.message} onRetry={() => nonAuthError.refetch()} />;
  }

  const tenants = tenantsQuery.data ?? null;
  const sites = sitesQuery.data ?? null;
  const apiKeys = keysQuery.data ?? null;

  return (
    <div>
      <div className="list-toolbar">
        <p className="form-hint" style={{ margin: 0 }}>Signed in as operator.</p>
        <button type="button" className="confirm-dialog-cancel" onClick={logOut}>
          Log out of operator mode
        </button>
      </div>

      {actionError && <p className="error">{actionError}</p>}

      {!tenants ? (
        <p>Loading tenants...</p>
      ) : (
        <div className="form-field">
          <label className="form-label" htmlFor="apikey-tenant">Tenant</label>
          <select
            id="apikey-tenant"
            className="form-select"
            value={selectedTenantId}
            onChange={(e) => {
              setSelectedTenantId(e.target.value);
              setSelectedSiteId("");
            }}
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
                disabled={createMutation.isPending}
                onClick={() => {
                  setActionError(null);
                  createMutation.mutate();
                }}
              >
                {createMutation.isPending ? "Creating..." : "Create key"}
              </button>
            </>
          )}
        </>
      )}

      <ConfirmDialog
        open={revokingKey !== null}
        message={`Revoke key "${revokingKey?.name || revokingKey?.keyId}"? This cannot be undone.`}
        confirmLabel="Revoke"
        onConfirm={() => {
          setActionError(null);
          if (revokingKey) revokeMutation.mutate(revokingKey.keyId);
        }}
        onCancel={() => setRevokingKey(null)}
      />
    </div>
  );
}
