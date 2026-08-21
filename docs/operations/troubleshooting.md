# Troubleshooting

**Status:** Current. Every entry below is a failure that actually happened in
this system, with the diagnosis that resolved it — not a list of things that
could theoretically go wrong.

The recurring theme, worth internalising before the specifics: **in this
system a fault usually surfaces somewhere other than where it is.** A config
problem reads as a storage error. A missing setting reads as a broken page. A
poisoned queue reads as silence. When the message and the cause seem
unrelated, that is the normal case here, not an unusual one.

---

## The Agent

### Crash loop: "No valid combination of account information found"

```
System.FormatException: No valid combination of account information found.
   at Azure.Storage.Queues.QueueServiceClient..ctor(String connectionString)
```

**Not a storage problem.** The connection string in `shared-config` is
encrypted (`enc:v1:`), and something stopped it being decrypted, so ciphertext
was handed to the SDK as if it were a connection string.

Scroll **up** in the log for the real cause, which passes as a warning the
agent shrugs off:

```
[Startup] Failed to decrypt downloaded config, using it as-is: ...
```

Then check, in order:

1. **Is `common-config.json` valid UTF-8 without a BOM?** A BOM makes
   `JsonNode.Parse` throw on byte zero. Guarded now, but a hand-edited blob
   can still be malformed.
2. **Is `CredentialEncryption:Key` present in the agent's `appsettings.json`?**
   Absent means no decryption is attempted at all.
3. **Does that key match Cloud's?** A mismatch fails silently — see below.

### Devices load, but cannot connect to anything

Symptom: config version looks right, capture fails, no obvious error.

Likely a **credential encryption key mismatch**. Cloud encrypted with key A,
the agent is decrypting with key B. `CredentialCipher.TryDecrypt` returns the
same result for "wrong key" and "not encrypted", by design — so nothing
throws and nothing logs.

Compare fingerprints rather than values:

```bash
python -c "import hashlib;print(hashlib.sha256(open('key.txt').read().strip().encode()).hexdigest()[:12])"
```

The Agent's `appsettings.json`, the Cloud app setting, and
`local.settings.json` should all produce the same 12 characters.

### `Messaging:AgentEventQueue is not configured`

Operational alerting is silently doing nothing. The agent is otherwise fine.
Add `AgentEventQueue` to `shared-config/common-config.json` and restart.

### No device configs found

```
[Startup] No device configs under the scoped prefix; falling back to the
flat container listing
```

Expected on a first start after the tenant/site scoping change, before
anything has been republished. **Not** expected afterwards — if you see it on
a settled system, the agent's `Agent:TenantId` / `Agent:SiteId` probably do
not match the prefix the blobs were published under.

---

## Cloud

### Messages piling up in `<queue>-poison`

The handler threw, Azure retried five times, and gave up. **The queue is
fine; the handler is not.** There is no App Insights on `vivnestcloud2`, so
the fastest diagnosis is to reproduce locally:

1. Stop anything holding the build outputs, then run the Functions host
   locally — it uses the same real storage.
2. Peek the poison message to get its `{PartitionKey, RowKey}`.
3. Re-enqueue it onto the live queue and watch the local console for the
   stack trace.

Beware re-enqueuing with `az storage message put --content '{...}'`: PowerShell
strips the quotes and you get a *second*, different failure
(`'P' is an invalid start of a property name`) that is your test harness, not
the bug.

**A real instance:** every `agent-events` message poisoned on
`DateTime 0001-01-01 has a Kind of Unspecified. Azure SDK requires it to be
UTC.` An entity had two `DateTime` fields where each row type set only one;
the unset one defaulted to a value Azure Tables refuses. Twelve unit tests
passed throughout, because the in-memory fake accepted any `DateTime`.

### The admin API returns nothing, but queues and timers work

Missing `Tables__*` app settings. Table names default to `""` with no
fallback, so only the endpoints touching *configured* tables work. Compare
the deployed app's settings against `local.settings.json` — the diff is the
answer. This is how 18 missing settings were found.

### Publishing returns `Published: false` with a reason, not an error

Working as designed. Common reasons:

| Reason contains | Meaning |
|---|---|
| `unchanged` | Content hash matches; no version burned. Not a failure. |
| `CredentialEncryption` | Key missing or malformed on Cloud. Publishing is blocked deliberately rather than falling back to plaintext. |
| `Cannot publish:` + warnings | Unresolved `RuntimeDeviceId`, missing `OwningAgentId`, or a capability with no registered projector. |

### An alert never arrives

Check in this order, because each stage looks identical from the next one up:

1. `OperationalAlert__Enabled` — off by default.
2. `Telegram__Enabled` — **currently `false` on `vivnestcloud2`**, so the
   pipeline runs to completion and delivers nothing.
3. `tblAgentAlertState` — a row means the throttle ran and allowed it.
4. The throttle itself: a repeat of the same error signature inside the
   cooldown is *supposed* to be silent.

A healthy agent and a broken alerting pipeline produce identical evidence:
nothing. Induce a fault if you need proof.

---

## Local development

### Build fails: "being used by another process"

A running Functions host or the Agent Updater is holding the DLLs. Either
stop it, or verify compilation without touching outputs:

```bash
dotnet msbuild <Project>/<Project>.csproj -t:Compile -v:q -nologo
```

MSBuild's own node-reuse processes can also hold stale handles —
`dotnet build-server shutdown` clears those, and they belong to your build,
not to anything running.

### Docker build says CACHED for `dotnet publish` after you changed code

Usually a legitimate layer-cache hit, but it looks exactly like a stale
image. Verify rather than trust it — type names live in the assembly's UTF-8
metadata:

```bash
docker run --rm --entrypoint /bin/sh <image> -c "grep -ac YourNewTypeName /app/Vivnest.Agent.dll"
```

Do **not** grep for a string literal: .NET stores those as UTF-16, so an
ASCII grep will report `0` for code that is present.

### `az` commands fail with "Account has previously been signed out"

The token expired. `az account show` may still succeed from cache while every
real call fails — so a command returning empty results can mean "not
authenticated", not "nothing found". Re-run `az login`.

---

## Diagnostic commands worth keeping

```bash
# What is the agent actually running, and did it load config?
docker logs --tail 40 vivnest-agent
```

```bash
# Is the queue backed up or poisoned?
az storage queue list --account-name stvivnestagent2 --account-key <key> -o table
```

```bash
# What does the agent think its config version is?
az storage entity query --account-name stvivnestagent2 --account-key <key> --table-name tblAgentHeartbeat -o json
```
