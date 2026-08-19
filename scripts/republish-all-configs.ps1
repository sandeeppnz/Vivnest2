# Republishes every agent and device configuration in a tenant/site, by
# calling the existing per-entity publish-config routes in a loop. There is
# no bulk endpoint; this is the script-shaped version of one.
#
# WHY YOU WOULD RUN THIS
#
# One pass does three jobs at once (see decision-log.md ADR-091, ADR-069,
# and the hash-ordering fix):
#
#   1. Writes the tenant/site-scoped blobs for the first time. Publishing is
#      the ONLY thing that creates them - deploying the new code does not.
#   2. Rewrites any device document still in the pre-`capabilities[]` legacy
#      shape into the current one.
#   3. Absorbs the one-off version bump from the "hash plaintext, then
#      encrypt" fix in one controlled batch, rather than letting it surprise
#      you later as scattered publishes and restarts.
#
# WHAT IT WILL DO TO A RUNNING SYSTEM - READ THIS
#
# Every successful publish dispatches a RestartAgent command to the owning
# agent (ADR-068). Republishing N devices owned by one agent therefore
# attempts N restarts. The dispatcher rejects the extras with AGENT_BUSY
# while one is already in flight (ADR-079), so they coalesce rather than
# stacking up - but the agent WILL restart, and any capture in progress is
# lost. Run it in a window where that is acceptable.
#
# Devices are published grouped by owning agent, and agents last, so each
# agent settles once rather than being restarted from both directions.
#
# A publish that changes nothing is a no-op by content hash and does NOT
# restart anything - so re-running this script is cheap and safe. Only the
# first pass after a deploy actually does work.
#
# USAGE
#
#   # See what would happen - this is the default, nothing is written:
#   .\republish-all-configs.ps1 -BaseUrl https://... -ApiKey <tenant-key>
#
#   # Actually do it:
#   .\republish-all-configs.ps1 -BaseUrl https://... -ApiKey <tenant-key> -Execute
#
#   # Just the agents, with a pause between each:
#   .\republish-all-configs.ps1 -BaseUrl ... -ApiKey ... -Execute -AgentsOnly -DelaySeconds 5
#
# The key must be a TENANT key, not an agent key - agent keys are rejected
# by every admin route by design.

[CmdletBinding()]
param(
    # e.g. https://vivnest-functions.azurewebsites.net  (no trailing /api)
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [Parameter(Mandatory = $true)]
    [string]$ApiKey,

    # Nothing is written unless this is passed. The default is a dry run
    # precisely because the side effect here is restarting live agents.
    [switch]$Execute,

    [switch]$AgentsOnly,

    [switch]$DevicesOnly,

    # Seconds to wait between publishes. 0 is fine for a handful of
    # entities; raise it if you would rather agents restart in sequence.
    [int]$DelaySeconds = 0,

    # Optional filters - the API key already scopes to a tenant, these
    # narrow it further when a site has entities you want to leave alone.
    [string]$SiteId,

    [string]$NameLike
)

$ErrorActionPreference = "Stop"

# Windows PowerShell 5.1 still negotiates TLS 1.0 by default against some
# endpoints; Azure will refuse it.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

if ($AgentsOnly -and $DevicesOnly) {
    throw "-AgentsOnly and -DevicesOnly are mutually exclusive."
}

$api = $BaseUrl.TrimEnd('/') + "/api"
$headers = @{ "x-api-key" = $ApiKey }

# Invoke-RestMethod throws on any non-2xx in 5.1, and the thrown object is
# where the status code lives - so every call goes through here rather than
# each site growing its own try/catch.
function Invoke-Api {
    param(
        [string]$Method,
        [string]$Path
    )

    try {
        $response = Invoke-RestMethod -Method $Method -Uri ($api + $Path) -Headers $headers
        return [PSCustomObject]@{ Ok = $true; Body = $response; Error = $null }
    }
    catch {
        $status = ""
        if ($_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
        }

        $message = $_.Exception.Message
        if ($status) {
            $message = "HTTP $status - $message"
        }

        return [PSCustomObject]@{ Ok = $false; Body = $null; Error = $message }
    }
}

function Test-Included {
    param($Entity)

    if ($SiteId -and $Entity.siteId -ne $SiteId) { return $false }
    if ($NameLike -and $Entity.name -notlike $NameLike) { return $false }

    return $true
}

Write-Host ""
Write-Host "Vivnest - republish all configurations" -ForegroundColor Cyan
Write-Host "  API:  $api"
if ($Execute) {
    Write-Host "  Mode: EXECUTE - this will publish and restart agents" -ForegroundColor Yellow
}
else {
    Write-Host "  Mode: DRY RUN - nothing will be written (pass -Execute to publish)" -ForegroundColor Green
}
Write-Host ""

# ---- who am I -------------------------------------------------------------
# Fails fast and legibly on a bad or agent-scoped key, rather than looking
# like "this tenant has no devices".
$who = Invoke-Api -Method GET -Path "/whoami"
if (-not $who.Ok) {
    throw "Could not authenticate against $api/whoami. $($who.Error)"
}

Write-Host "Authenticated as tenant '$($who.Body.tenantId)', site '$($who.Body.siteId)'."
Write-Host ""

# ---- gather ---------------------------------------------------------------
$agents = @()
$devices = @()

if (-not $DevicesOnly) {
    $result = Invoke-Api -Method GET -Path "/agents-registry-admin"
    if (-not $result.Ok) { throw "Failed to list agents. $($result.Error)" }
    $agents = @($result.Body | Where-Object { Test-Included $_ })
}

if (-not $AgentsOnly) {
    $result = Invoke-Api -Method GET -Path "/devices-registry-admin"
    if (-not $result.Ok) { throw "Failed to list devices. $($result.Error)" }
    $devices = @($result.Body | Where-Object { Test-Included $_ })
}

# Devices grouped by owning agent so one agent settles at a time; agents
# published afterwards, so the last restart an agent sees is the one
# carrying everything.
$devices = @($devices | Sort-Object -Property owningAgentId, name)
$agents = @($agents | Sort-Object -Property name)

Write-Host "Found $($agents.Count) agent(s) and $($devices.Count) device(s) in scope."
Write-Host ""

$results = New-Object System.Collections.ArrayList

function Publish-Entity {
    param(
        [string]$Kind,       # "Device" | "Agent"
        [string]$Id,
        [string]$Name,
        [string]$Route
    )

    if (-not $Execute) {
        [void]$results.Add([PSCustomObject]@{
            Kind = $Kind; Name = $Name; Id = $Id; Outcome = "would publish"; Reason = ""
        })

        Write-Host ("  [dry run] {0,-7} {1}" -f $Kind, $Name)
        return
    }

    $response = Invoke-Api -Method POST -Path $Route

    if (-not $response.Ok) {
        [void]$results.Add([PSCustomObject]@{
            Kind = $Kind; Name = $Name; Id = $Id; Outcome = "FAILED"; Reason = $response.Error
        })

        Write-Host ("  {0,-7} {1} - FAILED: {2}" -f $Kind, $Name, $response.Error) -ForegroundColor Red
        return
    }

    # A blocked or unchanged publish is a 200 with Published:false and a
    # Reason - not an HTTP error. Checking only the status code here would
    # report a warnings-blocked device as a success.
    if ($response.Body.published) {
        [void]$results.Add([PSCustomObject]@{
            Kind = $Kind; Name = $Name; Id = $Id; Outcome = "published"; Reason = ""
        })

        Write-Host ("  {0,-7} {1} - published" -f $Kind, $Name) -ForegroundColor Green
    }
    else {
        $reason = $response.Body.reason
        $outcome = "blocked"
        $colour = "Yellow"

        if ($reason -and $reason -match "unchanged") {
            $outcome = "unchanged"
            $colour = "DarkGray"
        }

        [void]$results.Add([PSCustomObject]@{
            Kind = $Kind; Name = $Name; Id = $Id; Outcome = $outcome; Reason = $reason
        })

        Write-Host ("  {0,-7} {1} - {2}: {3}" -f $Kind, $Name, $outcome, $reason) -ForegroundColor $colour
    }

    if ($DelaySeconds -gt 0) {
        Start-Sleep -Seconds $DelaySeconds
    }
}

# ---- devices first --------------------------------------------------------
if ($devices.Count -gt 0) {
    Write-Host "Devices:" -ForegroundColor Cyan

    foreach ($device in $devices) {
        Publish-Entity -Kind "Device" -Id $device.deviceId -Name $device.name `
            -Route "/devices-registry-admin/$($device.deviceId)/publish-config"
    }

    Write-Host ""
}

# ---- then agents ----------------------------------------------------------
if ($agents.Count -gt 0) {
    Write-Host "Agents:" -ForegroundColor Cyan

    foreach ($agent in $agents) {
        Publish-Entity -Kind "Agent" -Id $agent.agentId -Name $agent.name `
            -Route "/agents-registry-admin/$($agent.agentId)/publish-config"
    }

    Write-Host ""
}

# ---- summary --------------------------------------------------------------
Write-Host "Summary" -ForegroundColor Cyan

$results |
    Group-Object -Property Outcome |
    Sort-Object -Property Name |
    ForEach-Object { Write-Host ("  {0,-14} {1}" -f $_.Name, $_.Count) }

$problems = @($results | Where-Object { $_.Outcome -eq "FAILED" -or $_.Outcome -eq "blocked" })

if ($problems.Count -gt 0) {
    Write-Host ""
    Write-Host "Needs attention:" -ForegroundColor Yellow
    $problems | Format-Table Kind, Name, Outcome, Reason -AutoSize | Out-String | Write-Host
}

Write-Host ""

if (-not $Execute) {
    Write-Host "Dry run only - nothing was published. Re-run with -Execute to apply." -ForegroundColor Green
    exit 0
}

# "blocked" is a real outcome needing a human (unresolved RuntimeDeviceId,
# a capability with no projector, a missing encryption key), so it is worth
# a non-zero exit for anything driving this from a pipeline. "unchanged" is
# not a problem and deliberately does not count.
if (@($results | Where-Object { $_.Outcome -eq "FAILED" }).Count -gt 0) {
    exit 1
}

exit 0
