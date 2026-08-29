
# Vivnest

Vivnest is an edge-first IoT device monitoring platform. The end target is
broader than cameras: any device or sensor (cameras, water meters, heat
pumps, soil sensors, etc.) across multiple verticals (home, commercial CCTV,
agriculture, industrial IoT) — see
[docs/architecture/vivnest-runtime-overview.md](docs/architecture/vivnest-runtime-overview.md)
for the target vision. **Today, only camera monitoring is implemented**: an
edge agent captures camera snapshots and heartbeats, an Azure-hosted cloud
side persists and processes them, and Telegram delivers notifications. The
solution is split into six projects:

- **Vivnest.Agent** — the executable worker/host that runs the capture and heartbeat workers on-device.
- **Vivnest.Core** — shared interfaces, domain models, and option types used across the whole solution.
- **Vivnest.Infrastructure** — agent-side concrete implementations (camera drivers, Azure Blob/Table/Queue storage, DI wiring).
- **Vivnest.Cloud** — cloud-side handlers, repositories, and services (Telegram, blob storage, device-event processing).
- **Vivnest.Cloud.Functions** — the Azure Functions host that runs `Vivnest.Cloud`'s handlers on a queue trigger.
- **Vivnest.Agent.Updater** — a small standalone executable, deployed alongside `Vivnest.Agent` on the host (never inside its container), that pulls and redeploys the latest Agent image on a Cloud-issued Deploy command — see [decision-log.md](docs/architecture/decision-log.md) ADR-028.

For where this is heading architecturally and what's planned next, see
[docs/architecture/](docs/architecture/) and [docs/roadmap/](docs/roadmap/) —
start with [docs/roadmap/EVOLUTION-PLAN.md](docs/roadmap/EVOLUTION-PLAN.md).

## Prerequisites

- .NET 10 SDK (Agent) / .NET 8 SDK (Core, Infrastructure, Cloud, Cloud.Functions)
- Azure Storage account (Blob, Table, Queue) — or Azurite for local development
- A Telegram bot token, if you want notifications

## Build

From the repository root:

```
dotnet build Vivnest.slnx
```

## Run

Agent (on-device worker):

```
dotnet run --project Vivnest.Agent
```

Cloud Functions host (requires the Azure Functions Core Tools):

```
func start
```
(run from `Vivnest.Cloud.Functions`)

## Configuration

Both hosts bind configuration to option classes in `Vivnest.Core.Options`.
`Vivnest.Agent` reads from `appsettings.json`; `Vivnest.Cloud.Functions`
reads from `local.settings.json` (using double-underscore section
separators, e.g. `Tables__DeviceEvents`). Key sections:

- `Agent` — tenant/site/agent identity
- `Storage` — `ConnectionString`, `BlobContainer`
- `Tables` — table names per entity (`AgentHeartbeat`, `DeviceHeartbeat`, `DeviceEvents`)
- `Messaging` — queue connection string and queue names
- `AgentHeartbeat` / `DeviceHeartbeat` — enabled flag + interval
- `Devices` — per-device camera settings (host, RTSP credentials, capture interval)
- `DeviceEvents` — enabled flag
- `Telegram` (Cloud.Functions only) — bot token, chat ID, enabled flag

Example `appsettings.json` shape for `Vivnest.Agent` (adjust to your environment):

```json
{
  "Agent": {
    "TenantId": "<tenant>",
    "SiteId": "<site>",
    "AgentId": "<guid>",
    "Name": "<agent name>"
  },
  "Storage": {
    "ConnectionString": "<STORAGE_CONNECTION_STRING>",
    "BlobContainer": "photos"
  },
  "Tables": {
    "AgentHeartbeat": "tblAgentHeartbeat",
    "DeviceHeartbeat": "tblDeviceHeartbeat",
    "DeviceEvents": "tblDeviceEvents"
  },
  "Messaging": {
    "ConnectionString": "<STORAGE_CONNECTION_STRING>",
    "CameraCapturedQueue": "camera-captured",
    "AgentHeartbeatQueue": "agent-heartbeats",
    "DeviceHeartbeatQueue": "device-heartbeats",
    "DeviceEventQueue": "device-events"
  },
  "AgentHeartbeat": { "Enabled": true, "HeartbeatInterval": "00:01:00" },
  "DeviceHeartbeat": { "Enabled": true, "HeartbeatInterval": "00:02:00" },
  "DeviceEvents": { "Enabled": true },
  "Devices": [
    {
      "DeviceId": "camera-001",
      "Type": "Camera",
      "Enabled": true,
      "LivenessInterval": "00:05:00",
      "WarningMultiplier": 3,
      "SnapshotInterval": "00:30:00",
      "Settings": {
        "Host": "<camera-ip>",
        "RtspUsername": "<rtsp-user>",
        "RtspPassword": "<rtsp-password>"
      }
    }
  ]
}
```

Replace the placeholder values with your own configuration. **Do not commit
real secrets to the repository** — see below.

`WarningMultiplier` controls how many missed `LivenessInterval`s a device
tolerates before the agent marks it `Warning` — defaults to 3 (a buffer
against one delayed probe causing a false alert) if omitted. Set it to `1`
per-device for anything where fast detection matters more than avoiding an
occasional false positive.

## Secrets and best practices

- Never commit real secrets (connection strings, API keys, passwords) to source control.
- For local development, use `dotnet user-secrets`, environment variables, or a gitignored `local.settings.json` / `appsettings.json`.
- In production, use a secrets manager such as Azure Key Vault and inject secrets at runtime via environment variables or a managed identity.
- If a secret was committed accidentally, rotate it and remove it from Git history (e.g. `git filter-repo` or BFG Repo-Cleaner).

## Testing

`Vivnest.Tests` (xUnit, in the solution) — run it with:

```
dotnet test Vivnest.Tests/Vivnest.Tests.csproj
```

93 tests across three areas:

- **Shared primitives** (`Vivnest.Core`) — the device-config runtime
  adapter, event RowKey formatting, free-text name matching, command-status
  terminality.
- **The configuration publish pipeline** (`Vivnest.Cloud`) — monotonic
  versioning, immutable version blobs, the manifest pointer, the
  content-hash no-op guard, ETag retry, rollback-as-a-new-version, and the
  tenant/site-scoped blob layout.
- **API auth** (`Vivnest.Cloud.Functions`) — the agent command callbacks
  driven through the real Function class with `AgentAuth:RequireApiKey`
  both off and on.

No Azure is required; storage is faked behind interfaces. What is *not*
covered: the Agent host process, the queue-triggered and timer-triggered
functions, and the dashboard. So treat a green run as "the tested paths did
not regress", not "the system works" — several defects this suite exists
because of were only ever found by running against real storage.

## Deployment

| Component | Command | Target |
|---|---|---|
| Cloud Functions | `scripts/deploy-cloud.ps1` | `vivnestcloud2` / `rg-vivnest-2` |
| Agent image | `scripts/build-and-push-agent.ps1 -Version x.y.z` | `vivnestagent2acr` / `rg-vivnest-2` |
| Agent container (on the host) | `scripts/update-agent.ps1` | local Docker |
| Dashboard | `Vivnest.Dashboard/deploy.ps1` | `vivnest-dashboard-2` / `rg-vivnest-2` |

**First-run bootstrap**: a fresh deployment has no API key yet. Open
the dashboard's login page with `?setup` appended (e.g.
`https://<dashboard-host>/?setup`) to reveal "First time? Set up with
the operator key" - it takes the Azure Functions host key (Portal →
Function App → App keys → `default`), creates the first tenant and
site, and mints a developer-role API key. The link is hidden by
default only to avoid advertising the operator tier on a public login
page; the flow itself is gated server-side by the host key, which
401s everything without it.

**Every deploy target is named in its script, deliberately.** Two V1
resources sit alongside the V2 ones and differ only by a suffix -
`vivnestcloudprod`, and `vivnest-dashboard` without the `-2`, both in
`rg-vivnest-dev`. Deploying V2 code onto either is silent and looks like
success. Two such traps were found and removed on 2026-08-23: the
Functions publish profiles (deleted; they pointed at `vivnestcloudprod`)
and the dashboard's hard-coded deployment token (rewritten to resolve
from the named app; it belonged to the V1 static web app). If you add a
deploy path, name the app and resource group in the script - never embed
an opaque token or profile that cannot be checked by reading it. See
[decision-log.md](docs/architecture/decision-log.md) ADR-094 for the full
environment map.


## Operations

- [docs/operations/configuration.md](docs/operations/configuration.md) — every
  configuration source, in load order, for both the Agent and Cloud.
- [docs/operations/deployment.md](docs/operations/deployment.md) — deploying
  the Functions app, the Agent image, an Agent onto a host, and the dashboard.
- [docs/operations/troubleshooting.md](docs/operations/troubleshooting.md) —
  failures that actually happened, and what they turned out to be.

## Contributing

Fork, create a feature branch, and open a pull request.

## License

See repository for license details (if present).

