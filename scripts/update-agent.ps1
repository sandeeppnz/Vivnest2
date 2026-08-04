# Run this ON THE HOST (not the dev machine), from C:\vivnest-agent.
# Pulls the latest Agent image from ACR and recreates the container from
# it - this is the "script to pull a new image" redeploy path from
# decision-log.md's ADR-020 follow-up (Watchtower is the natural next step
# once there's more than one agent/host; this is the right-sized version
# for one).
#
# ASSUMPTIONS - check these against how the container is actually running
# today before trusting this script on a live agent:
#   - Container name is "vivnest-agent". If yours differs, change
#     $ContainerName below.
#   - appsettings.json lives at C:\vivnest-agent\appsettings.json, mounted
#     to /app/appsettings.json (per the Dockerfile's own comment on why
#     it's not baked into the image).
#   - HomeAssistant__BaseUrl is overridden to host.docker.internal - the
#     fix from the earlier Docker-networking bug (appsettings.json's own
#     value is http://localhost:8123/, correct for local dotnet run, wrong
#     inside a container). If you're not running Home Assistant, or have
#     other env var overrides on the real container (check with
#     `docker inspect vivnest-agent` first if unsure), adjust accordingly.
#   - Assumes you're already logged in to the registry (`docker login
#     vivnestagentacr.azurecr.io` once, credentials cached) - this script
#     doesn't handle auth itself.

$ErrorActionPreference = "Stop"

$Image = "vivnestagentacr.azurecr.io/vivnest-agent:latest"
$ContainerName = "vivnest-agent"
$AppSettingsPath = "C:\vivnest-agent\appsettings.json"

Write-Host "Pulling $Image ..."
docker pull $Image

Write-Host "Stopping and removing existing container (if any) ..."
docker stop $ContainerName 2>$null
docker rm $ContainerName 2>$null

Write-Host "Starting new container ..."
docker run -d `
    --name $ContainerName `
    --restart unless-stopped `
    -v "${AppSettingsPath}:/app/appsettings.json" `
    -e "HomeAssistant__BaseUrl=http://host.docker.internal:8123/" `
    $Image

Write-Host ""
Write-Host "Waiting a few seconds, then showing recent logs to confirm it started..."
Start-Sleep -Seconds 5
docker logs --tail 30 $ContainerName
