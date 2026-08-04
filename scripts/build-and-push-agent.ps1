# Run this on the DEV machine, from anywhere (resolves the repo root off
# its own location). Builds the Agent image with the current commit baked
# in as FirmwareVersion (see Vivnest.Agent/Dockerfile and decision-log.md's
# ADR-020 follow-up), verifies it landed correctly, then pushes to ACR.
#
# To actually deploy this to the running agent, follow up with
# update-agent.ps1 on the host.

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

$BuildVersion = (git rev-parse --short HEAD).Trim()
if ([string]::IsNullOrWhiteSpace($BuildVersion)) {
    throw "Could not resolve the current commit SHA (git rev-parse failed)."
}

$FullImage = "${Registry}/${ImageName}:latest"

Write-Host "Building $FullImage (BUILD_VERSION=$BuildVersion)..."
docker build `
    --build-arg "BUILD_VERSION=$BuildVersion" `
    -t $FullImage `
    -f "Vivnest.Agent/Dockerfile" `
    .
if ($LASTEXITCODE -ne 0) {
    throw "docker build failed."
}

Write-Host "Verifying the baked-in FirmwareVersion..."
$EnvOutput = docker run --rm --entrypoint env $FullImage
$FirmwareLine = $EnvOutput | Select-String "^Agent__FirmwareVersion="

if (-not $FirmwareLine) {
    throw "Agent__FirmwareVersion was not found in the built image - aborting before push."
}

$ActualVersion = $FirmwareLine.ToString().Split("=", 2)[1]

if ($ActualVersion -ne $BuildVersion) {
    throw "Agent__FirmwareVersion is '$ActualVersion', expected '$BuildVersion' - aborting before push."
}

Write-Host "Confirmed: Agent__FirmwareVersion=$ActualVersion"

Write-Host "Pushing $FullImage..."
docker push $FullImage
if ($LASTEXITCODE -ne 0) {
    throw "docker push failed."
}

Write-Host ""
Write-Host "Done. Pushed $FullImage (FirmwareVersion=$BuildVersion)."
Write-Host "Run update-agent.ps1 on the host to deploy it."
