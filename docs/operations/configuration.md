# Configuration — where every setting actually comes from

**Status:** Current. Describes the configuration surface as built, verified
against `Vivnest.Agent/Program.cs` and the deployed environment on
2026-08-21.

This exists because configuration in Vivnest is layered across five sources
per process, some of them remote and one of them encrypted — and getting the
layering wrong has caused two real incidents, both recorded at the bottom.
Nothing else in the docs answers "where does this value come from."

---

## The Agent

`Vivnest.Agent` builds its configuration in a deliberate order. Later
sources win, except environment variables, which are kept last on purpose.

| # | Source | Where | Notes |
|---|---|---|---|
| 1 | `appsettings.json` | Next to the executable — **bind-mounted into the container** | Loaded by `Host.CreateApplicationBuilder` before any Vivnest code runs. Written by `Vivnest.Agent.Updater` at registration. |
| 2 | Environment variables | Process | Loaded at the same time as 1. |
| 3 | Shared config | `shared-config/common-config.json` blob | Queue names, table names, intervals. One blob, every agent. |
| 4 | Agent config | `agent-config/{tenant}/{site}/{runtimeAgentId}.json` blob | This agent's own `AiClassification`, `Name`, config version. |
| 5 | Device configs | `device-config/{tenant}/{site}/{runtimeDeviceId}.json` blobs | **Low-type agents only.** Assembled into `Devices[]`. |
| 6 | Local secrets | `common-config.secrets.json`, `{agentId}.secrets.json`, `device-config/{id}.secrets.json` | Gitignored, never uploaded. Loaded **unconditionally**. |

**Sources 3–5 are inserted *before* environment variables, not appended.**
`InsertConfigSourceBeforeEnvVars` walks `configuration.Sources`, finds the
`EnvironmentVariablesConfigurationSource`, and inserts ahead of it. So an env
var always beats a downloaded blob — which is what makes
`-e "HomeAssistant__BaseUrl=..."` on `docker run` work as an override.

**`LoadLocalSettings` swaps 3 and 4 for local files** of the same shape, for
development. It is `false` in every deployed configuration, and the Updater
forces it to `false` when it writes `appsettings.json`. Secrets (source 6)
load either way.

### Two things about this that surprise people

**The credential encryption key is read from source 1, not source 6.** It has
to be: it is needed to *decrypt* sources 3–5, so it cannot live in a file
loaded after them. ADR-085 originally put it in `common-config.secrets.json`
and ADR-086 moved it, for exactly this reason.

**Device identity is not in `appsettings.json`.** A Low-type agent lists the
`device-config` container under its own tenant/site prefix and keeps the
blobs whose `OwningAgentId` matches. Adding a device to an agent is a Cloud
publish, not an agent config edit.

---

## Cloud (`Vivnest.Cloud.Functions`)

One flat namespace, from `local.settings.json` locally and Azure app
settings in production. `__` is the nesting separator (`Tables__ApiKeys` →
`Tables:ApiKeys`).

47 settings on `vivnestcloud2` as of 2026-08-21. The groups:

| Group | Examples | Notes |
|---|---|---|
| Storage | `Storage__ConnectionString`, `AzureWebJobsStorage` | Account-level connection strings. |
| Tables | `Tables__AgentRegistry`, `Tables__DeviceConfiguration`, … (17) | **Every one defaults to `""` with no fallback** — see the trap below. |
| Events | `DeviceEvents__Enabled`, `AgentEvents__Enabled` | |
| Timers | `HealthMonitorCronSchedule`, `DeviceEventRetentionCronSchedule`, `CommandExpiryCronSchedule` | **Single underscore, deliberately** — the `%…%` timer placeholder resolver looks up the literal name. |
| Notifications | `Telegram__Enabled`, `Telegram__BotToken`, `Telegram__ChatId` | |
| Security | `CredentialEncryption__Key`, `AgentAuth__RequireApiKey` | |
| Alerting | `OperationalAlert__Enabled`, `Tables__AgentAlertState` | Sprint 8; off unless enabled. |

### The trap that bit hardest

**`TablesOptions` defaults every table name to the empty string, with no
fallback.** `AzureTableStore<T>`'s constructor calls
`GetTableClient(name).CreateIfNotExists()`, so a missing `Tables__*` setting
does not degrade — it throws, usually at the first request that touches that
table rather than at startup.

On 2026-08-20, `vivnestcloud2` was missing **18 settings** (15 `Tables__*`,
two `CommandExpiry__*`, and `CredentialEncryption__Key`). The symptom was not
"a setting is missing"; it was that the entire admin API could not respond,
while the queue and timer functions worked fine — because those used the
handful of table names that *were* configured.

If you add a table, add its setting to **both** `local.settings.json` and the
deployed app, in the same sitting.

---

## Encryption at rest and in transit

`CredentialCipher` (ADR-085) encrypts credential-shaped keys —
anything whose name contains `password`, `accesstoken`, `secret` or
`connectionstring` — as `enc:v1:` AES-256-GCM before publishing.

- Cloud encrypts on publish; the Agent decrypts on load.
- **The same key must be on both sides.** A mismatch does not error: the
  Agent's `TryDecrypt` treats "wrong key" and "not encrypted" identically, so
  you get a clean-looking startup and a device that cannot authenticate.
- Missing key on Cloud blocks publishing outright rather than falling back to
  plaintext, which is deliberate — a security control that degrades silently
  is not one.

**Not covered by this:** credentials in `tblDeviceRegistry.Settings` are
plaintext at rest and returned verbatim by the admin API. Known, deferred,
pending a decrypt-vs-mask decision — see the cleanup backlog in
[EVOLUTION-PLAN.md](../roadmap/EVOLUTION-PLAN.md).

---

## Two incidents worth learning from

**A UTF-8 BOM broke every agent (2026-08-20).** An edit wrote
`common-config.json` with a BOM; it was uploaded to `shared-config`. The BOM
made `JsonNode.Parse` throw *inside the decryption step*, so the config was
used still-encrypted, and the agent died several layers away on
`QueueServiceClient("enc:v1:…")` reporting *"No valid combination of account
information found"* — a message naming neither JSON, nor a BOM, nor the file.

Now guarded twice: `DecryptConfigBytes` strips a leading BOM, and
`ConfigFileEncodingTests` fails the build if one reaches the repo. The
general lesson holds regardless: **a config problem in this system usually
surfaces as a storage or credential error somewhere else.**

**A version bump that was really a config gap (2026-08-20).** `ImageVersion`
could only be changed by `Install` or `Move`, both of which mint a new
`InstallationId` — which the Updater has stored and reports against. Fixed by
adding a route that changes it in place (ADR-094 era). Configuration that can
only be changed by recreating the thing it configures is a design smell worth
noticing early.
