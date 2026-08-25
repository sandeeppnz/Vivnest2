# Publishes Vivnest.Cloud.Functions and deploys it to the V2 Function App.
#
# This exists because the repo previously had no Functions deploy path at
# all except two Visual Studio publish profiles - and both of them targeted
# `vivnestcloudprod` in `rg-vivnest-dev`, the V1 environment. Hitting
# Publish from this repo would have deployed V2 code to V1. They were
# deleted on 2026-08-23; this replaces them.
#
# The app and resource group are named here rather than embedded in an
# opaque profile, so the target is reviewable before it runs. See
# decision-log.md ADR-094 for the environment map: anything named
# *-dev / vivnestcloudprod / vivnest-dashboard (no `-2`) is V1.
#
# Requires: az login, and the Azure Functions Core Tools are NOT needed -
# this is a plain zip deploy.

param(
    [string]$AppName = "vivnestcloud2",
    [string]$ResourceGroup = "rg-vivnest-2"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "artifacts/cloud-publish"
$zipPath = Join-Path $repoRoot "artifacts/cloud-publish.zip"

Write-Host "Publishing Vivnest.Cloud.Functions..."

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

New-Item -ItemType Directory -Force -Path (Split-Path $publishDir) | Out-Null

dotnet publish (Join-Path $repoRoot "Vivnest.Cloud.Functions/Vivnest.Cloud.Functions.csproj") `
    -c Release -o $publishDir --nologo

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# NOT Compress-Archive. It silently omits dot-directories, so the package
# ends up without `.azurefunctions` and Kudu rejects it with
# "Cannot find required .azurefunctions directory at root level in the
# .zip package" - a content-validation failure that looks like a broken
# build rather than a broken zip.
#
# And NOT CreateFromDirectory either: under Windows PowerShell 5.1 the
# .NET Framework ZipFile writes entry names with BACKSLASHES, which
# violates the zip spec (and made the .azurefunctions check below fail,
# since it matches on "/"). Building the entries by hand pins forward
# slashes on every PowerShell edition.
Write-Host "Packaging (including dot-directories)..."

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$archive = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)

try {
    $root = (Resolve-Path $publishDir).Path

    Get-ChildItem $publishDir -Recurse -File -Force | ForEach-Object {
        $entryName = $_.FullName.Substring($root.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entryName) | Out-Null
    }
}
finally {
    $archive.Dispose()
}

$entries = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
$hasRuntime = @($entries.Entries | Where-Object { $_.FullName -like "*.azurefunctions/*" }).Count
$entries.Dispose()

if ($hasRuntime -eq 0) {
    throw "Package is missing the .azurefunctions directory - Kudu will reject it. Zip it with a tool that includes dot-directories."
}

Write-Host "Deploying to $AppName ($ResourceGroup)..."

az functionapp deployment source config-zip `
    --name $AppName `
    --resource-group $ResourceGroup `
    --src $zipPath `
    --timeout 900

if ($LASTEXITCODE -ne 0) { throw "Zip deployment failed." }

Write-Host ""
Write-Host "Done. Deployed to $AppName."
