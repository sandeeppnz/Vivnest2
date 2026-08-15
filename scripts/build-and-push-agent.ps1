# Run this on the DEV machine, from anywhere (resolves the repo root off
# its own location). Builds the Agent image with the current commit baked
# in as FirmwareVersion (see Vivnest.Agent/Dockerfile and decision-log.md's
# ADR-020 follow-up), verifies it landed correctly, then pushes to ACR.
#
# To actually deploy this to the running agent, follow up with
# update-agent.ps1 on the host.
#
# -Version (decision-log.md ADR-073): pass a real semver (e.g. "1.4.0") to
# also tag+push that version alongside :latest, and to bake it in as
# FirmwareVersion instead of the git SHA - the real image tag an
# AgentInstallation.ImageVersion can then be enforced against (see
# AgentDeployer/DeployCommandQueueMessage). Omit it and this script
# behaves exactly as before: SHA-stamped, :latest only.
param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$RegistryName = "vivnestagentacr"
$Registry = "$RegistryName.azurecr.io"
$ImageName = "vivnest-agent"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

Set-Location $RepoRoot

Write-Host "Checking Azure CLI login..."
az account show *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Not logged in - running az login..."
    az login
    if ($LASTEXITCODE -ne 0) {
        throw "az login failed. Sign in manually and re-run this script."
    }
}

Write-Host "Logging Docker in to $Registry..."
az acr login --name $RegistryName
if ($LASTEXITCODE -ne 0) {
    throw "az acr login failed."
}

$GitSha = (git rev-parse --short HEAD).Trim()
if ([string]::IsNullOrWhiteSpace($GitSha)) {
    throw "Could not resolve the current commit SHA (git rev-parse failed)."
}

# ADR-073 - when -Version is given, that's what gets baked in as
# FirmwareVersion (what AgentHeartbeat.FirmwareVersion will report, and
# what an AgentInstallation.ImageVersion is actually compared against) -
# not the git SHA. Falls back to the SHA, unchanged, when -Version is
# omitted.
$BuildVersion = if ($Version) { $Version } else { $GitSha }

$LatestImage = "${Registry}/${ImageName}:latest"
$VersionedImage = if ($Version) { "${Registry}/${ImageName}:${Version}" } else { $null }

Write-Host "Building $LatestImage (BUILD_VERSION=$BuildVersion)..."
docker build `
    --build-arg "BUILD_VERSION=$BuildVersion" `
    -t $LatestImage `
    -f "Vivnest.Agent/Dockerfile" `
    .
if ($LASTEXITCODE -ne 0) {
    throw "docker build failed."
}

if ($VersionedImage) {
    docker tag $LatestImage $VersionedImage
    if ($LASTEXITCODE -ne 0) {
        throw "docker tag failed."
    }
}

Write-Host "Verifying the baked-in FirmwareVersion..."
$EnvOutput = docker run --rm --entrypoint env $LatestImage
$FirmwareLine = $EnvOutput | Select-String "^Agent__FirmwareVersion="

if (-not $FirmwareLine) {
    throw "Agent__FirmwareVersion was not found in the built image - aborting before push."
}

$ActualVersion = $FirmwareLine.ToString().Split("=", 2)[1]

if ($ActualVersion -ne $BuildVersion) {
    throw "Agent__FirmwareVersion is '$ActualVersion', expected '$BuildVersion' - aborting before push."
}

Write-Host "Confirmed: Agent__FirmwareVersion=$ActualVersion"

Write-Host "Pushing $LatestImage..."
docker push $LatestImage
if ($LASTEXITCODE -ne 0) {
    throw "docker push failed."
}

if ($VersionedImage) {
    Write-Host "Pushing $VersionedImage..."
    docker push $VersionedImage
    if ($LASTEXITCODE -ne 0) {
        throw "docker push failed."
    }
}

Write-Host ""
Write-Host "Done. Pushed $LatestImage$(if ($VersionedImage) { " and $VersionedImage" }) (FirmwareVersion=$BuildVersion)."
Write-Host "Run update-agent.ps1 on the host to deploy it, or set the target AgentInstallation's ImageVersion to '$BuildVersion' and use the dashboard's Install/Move/Deploy actions."
