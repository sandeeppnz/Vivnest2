# Vivnest Dashboard

The operator UI for Vivnest — React 19 + TypeScript + Vite, deployed as an
Azure Static Web App. It is a pure client of the `Vivnest.Cloud.Functions`
REST API; it has no server of its own and talks to no storage directly.

Replaces the stock Vite template readme that sat here until 2026-08-21.

## Running it

```
npm install
npm run dev        # Vite dev server, HMR
npm run build      # tsc -b && vite build
npm run lint       # oxlint
```

`VITE_API_BASE_URL` points it at an API. Without it, `api.ts` falls back to
`http://localhost:7071/api` — the Azure Functions default port, which is
**not** the port `func host start` uses if you have overridden it (this repo
has been run on 7003). Set it explicitly rather than relying on the fallback:

```
# .env  (gitignored — .env.example shows the shape)
VITE_API_BASE_URL=https://<function-app>.azurewebsites.net/api
```

Note the trailing `/api`: routes are appended directly, so omitting it
produces 404s that look like missing endpoints rather than a misconfigured
base URL.

## How it authenticates

There is no login. The dashboard asks for an **API key** and sends it as the
`x-api-key` header on every request — the same tenant-key model
`ApiFunctionBase` enforces server-side (ADR-012).

- `ApiKeyGate` prompts for a tenant key and stores it in `localStorage`
  under `vivnest.apiKey`.
- `OperatorKeyGate` guards the destructive installation actions separately.

Two consequences worth knowing before treating this as secure:

1. **The key lives in `localStorage`**, so anything able to run script in
   this origin can read it. This is an operator tool for a trusted machine,
   not a public-facing app.
2. **The key carries the tenant scope, not a user identity.** The audit
   trail therefore records *what* changed, not *who* changed it.

An agent-scoped key is rejected here by design: `AuthenticateAsync` refuses
any key carrying an `AgentId`, so an agent's own credential cannot drive the
admin surface.

## What is in `src/`

Flat by choice — 48 files, no folder hierarchy. Roughly:

| Group | Files |
|---|---|
| Shell | `App.tsx`, `main.tsx`, `Sidebar.tsx`, `BottomTabBar.tsx`, `AdminDrawer.tsx` |
| Agents | `AgentList`, `AgentRow`, `AgentDetail`, `AgentMetricsChart`, `CommandHistory` |
| Devices | `DeviceList`, `DeviceRow`, `DeviceDetail`, `DeviceEventList`, `CaptureGallery` |
| Admin | `*RegistryAdmin`, `*FormModal`, `CapabilitiesAdmin`, `MachinesAdmin`, `ApiKeysAdmin`, `DeviceTypesAdmin`, `AgentInstallationsAdmin` |
| Config preview | `ProjectedConfigModal`, `AgentProjectedConfigModal` |
| Shared | `api.ts`, `format.ts`, `icons.tsx`, `errorTips.ts`, `eventDescriptions.ts`, `ErrorBanner`, `ConfirmDialog` |

`api.ts` is the single place the API surface is described. When a route
changes server-side, that is the file that changes here.

## Styling

Hand-rolled CSS (`App.css`, `index.css`) — no framework, no CSS-in-JS. That
was a deliberate call during the visual redesign; keep additions in those
files rather than introducing a styling dependency for one component.

## Deployment

Azure Static Web App `vivnest-dashboard-2` in `rg-vivnest-2`, deployed via
the SWA CLI. Static Web Apps is not offered in New Zealand North (where the
Function App lives), so it sits in the nearest supported region — see
ADR-094 for the full environment map, and note that `vivnest-dashboard`
without the `-2` belongs to the V1 generation and is a different app.

`deploy.ps1` resolves the deployment token from that app name at run time
via `az`. It must never carry a literal token again: the version before
2026-08-23 hard-coded one belonging to `vivnest-dashboard` in
`rg-vivnest-dev`, so running it from this repo published V2 code — built
against the V2 Functions API — onto the **V1** site. The script named no
app, so reading it could not reveal where it pointed. The same trap existed
in `Vivnest.Cloud.Functions/Properties/PublishProfiles`, whose only two
profiles targeted `vivnestcloudprod` in `rg-vivnest-dev`. They were
deleted on 2026-08-23 and replaced by `scripts/deploy-cloud.ps1`, which
names its target.

## Things that will bite you

- **A blank list is usually auth, not emptiness.** A wrong or revoked key
  returns 401 and several views render empty rather than surfacing the
  error. Check the network tab before concluding there is no data.
- **The API base URL fallback is a different port** from the one this repo's
  Functions host has been run on. See above.
- **`DevicesOnly` keys exist.** Such a key gets 403 on agent and admin
  routes, which looks like a broken page rather than a permission boundary.
