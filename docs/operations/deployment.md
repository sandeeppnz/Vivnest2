# Deployment

**Status:** Current, verified by performing every step below on 2026-08-20/21.

Four things deploy independently: the Cloud Functions app, the Agent
container image, the Agent onto a host, and the dashboard. They are not
coupled, and the order between them only matters in the two cases called out
below.

The environment map is [ADR-094](../architecture/decision-log.md). Short
version: **everything lives in `rg-vivnest-2`**. Anything in `rg-vivnest-dev`
is the V1 generation and belongs to a different codebase — do not deploy to
it, and do not read its settings as a reference.

| Component | Resource |
|---|---|
| Functions | `vivnestcloud2` |
| Storage | `stvivnestagent2` |
| Registry | `vivnestagent2acr` |
| Dashboard | `vivnest-dashboard-2` |

---

## 1. Cloud Functions

```bash
cd Vivnest.Cloud.Functions && func azure functionapp publish vivnestcloud2 --dotnet-isolated
```

Requires a live `az login`. Takes a few minutes; the output lists every
registered function, which is the quickest confirmation that a new trigger
actually landed.

**Settings do not travel with the deploy.** If the change needs a new app
setting, set it *first* — table names in particular, because
`TablesOptions` defaults them to `""` and the failure appears at first use
rather than at startup. See [configuration.md](configuration.md).

Smoke test after deploying:

```bash
curl -s -o /dev/null -w "%{http_code}\n" -H "x-api-key: <tenant-key>" https://<app>.azurewebsites.net/api/whoami
```

`200` means routing, auth and at least one table binding are all working.

---

## 2. Agent image

```bash
./scripts/build-and-push-agent.ps1 -Version 1.1.1
```

Builds from the current working tree, bakes the version in as
`Agent__FirmwareVersion`, verifies that stuck, and pushes both `:1.1.1` and
`:latest`.

**Push your commits first.** The version is what agents report on their
heartbeat, so an image built from unpushed work reports a version nobody can
trace to source.

Omitting `-Version` bakes the git SHA and pushes `:latest` only. Prefer a
semver: it is what makes `AgentInstallation.ImageVersion` meaningful rather
than permanently `NeverDeployed`.

**Verify the image rather than trusting the build output** — a legitimate
layer-cache hit and a stale image look identical. See the note in
[troubleshooting.md](troubleshooting.md#docker-build-says-cached-for-dotnet-publish-after-you-changed-code).

---

## 3. Agent onto a host

Two routes.

**Break-glass, run on the host:**

```bash
./scripts/update-agent.ps1
```

Pulls `:latest`, recreates the container. Still the documented manual
procedure. Note it is pinned to `:latest` — to deploy a *specific* tag you
need the Updater route or a manual `docker run`.

**Normal route:** set the desired version, then deploy through the Updater,
which consumes `agent-deploy-commands` and does the `pull/stop/rm/run` on the
host.

```bash
curl -X POST ".../api/agent-installations-admin/{agentId}/image-version" \
  -H "x-api-key: <key>" -H "Content-Type: application/json" \
  -d '{"ImageVersion":"1.1.1"}'
```

**Order matters here.** Deploy first, then set `ImageVersion` — or accept
that between the two the dashboard shows the agent as out of date, because
it is comparing a new desired version against the old reported one. Correct
behaviour, alarming appearance.

Do **not** use `Install` or `Move` to change a version. Both decommission the
installation and mint a new `InstallationId`, which the Updater has stored and
reports `deploy-complete` against.

Whichever route, recreating the container preserves nothing automatically —
confirm the mount and env survive:

```bash
docker inspect vivnest-agent | grep -A5 Mounts
```

---

## 4. Dashboard

Azure Static Web App `vivnest-dashboard-2`, via the SWA CLI. Set
`VITE_API_BASE_URL` at build time — it is baked into the bundle, not read at
runtime. See [the dashboard readme](../../Vivnest.Dashboard/README.md).

---

## Publishing configuration

Deploying code does **not** publish configuration. Scoped blobs are only
written by a publish, and a publish only happens when someone asks for one.

```bash
./scripts/republish-all-configs.ps1 -BaseUrl https://<app>.azurewebsites.net -ApiKey <key>
```

Dry run by default. Add `-Execute` to apply.

Each successful publish **restarts the owning agent**. Unchanged content is a
no-op and restarts nothing, so re-running is cheap — only the first pass
after a change does work.

---

## What deploying does not cover

- **Blob lifecycle.** No retention policy exists on `stvivnestagent2`;
  captures are not aged out. Accepted as a known difference (ADR-094), not
  fixed. `devops/blob-lifecycle/` has the policy file if that changes.
- **Table creation.** Tables are created on first use by
  `AzureTableStore<T>`, provided the name is configured.
- **Queue creation.** Created on first publish by `AzureQueuePublisher`.
- **Agent secrets.** `*.secrets.json` files are local to each host and never
  travel through git or blob storage.

---

## A post-deploy checklist that would have caught this week's failures

1. `whoami` returns 200.
2. The agent heartbeat shows the expected `FirmwareVersion` **and** a recent
   `LastHeartbeatUtc` — a stale heartbeat with the right version means it
   started and then died.
3. No messages in any `*-poison` queue.
4. The agent log contains no `Failed to decrypt downloaded config`.
5. If configuration changed, one publish, and the device heartbeat reports
   the new `ConfigurationVersion`.
