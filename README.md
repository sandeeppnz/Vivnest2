
# Vivnest

Vivnest is an edge-first IoT device monitoring platform. The end target is
broader than cameras: any device or sensor (cameras, water meters, heat
pumps, soil sensors, etc.) across multiple verticals (home, commercial CCTV,
agriculture, industrial IoT) — see
[docs/architecture/vivnest-runtime-overview.md](docs/architecture/vivnest-runtime-overview.md)
for the target vision. **Today, only camera monitoring is implemented**: an
edge agent captures camera snapshots and heartbeats, an Azure-hosted cloud
side persists and processes them, and Telegram delivers notifications. The
solution is split into five projects:

- **Vivnest.Agent** — the executable worker/host that runs the capture and heartbeat workers on-device.
- **Vivnest.Core** — shared interfaces, domain models, and option types used across the whole solution.
- **Vivnest.Infrastructure** — agent-side concrete implementations (camera drivers, Azure Blob/Table/Queue storage, DI wiring).
- **Vivnest.Cloud** — cloud-side handlers, repositories, and services (Telegram, blob storage, device-event processing).
- **Vivnest.Cloud.Functions** — the Azure Functions host that runs `Vivnest.Cloud`'s handlers on a queue trigger.

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

No automated test project exists yet. This is a known gap, not a
placeholder — see [docs/roadmap/EVOLUTION-PLAN.md](docs/roadmap/EVOLUTION-PLAN.md).

## Contributing

Fork, create a feature branch, and open a pull request.

## License

See repository for license details (if present).

