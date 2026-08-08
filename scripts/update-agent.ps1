# Run this ON THE HOST (not the dev machine), from C:\vivnest-agent by
# default. Pulls the latest Agent image from ACR and recreates the
# container from it - this is the "script to pull a new image" redeploy
# path from decision-log.md's ADR-020 follow-up (Watchtower is the
# natural next step once there's more than one agent/host; this is the
# right-sized version for a couple).
#
# Both agent roles (Capture and Ai, ADR-035) share this exact same image -
# only the mounted appsettings.json (Agent:Role, Agent:AgentId) differs.
# Running two agents on one Docker host: call this script twice with
# different -ContainerName/-AppSettingsPath, one per agent, e.g.
#   .\update-agent.ps1 -ContainerName vivnest-agent-capture -AppSettingsPath C:\vivnest-agent-capture\appsettings.json
#   .\update-agent.ps1 -ContainerName vivnest-agent-ai -AppSettingsPath C:\vivnest-agent-ai\appsettings.json
#
# ASSUMPTIONS - check these against how the container is actually running
# today before trusting this script on a live agent:
#   - HomeAssistant__BaseUrl is overridden to host.docker.internal - the
#     fix from the earlier Docker-networking bug (appsettings.json's own
#     value is http://localhost:8123/, correct for local dotnet run, wrong
#     inside a container). Harmless for an Ai-role agent (nothing there
#     reads HomeAssistant config). If you're not running Home Assistant,
#     or have other env var overrides on the real container (check with
#     `docker inspect <container>` first if unsure), adjust accordingly.
#   - Assumes you're already logged in to the registry (`docker login
#     vivnestagentacr.azurecr.io` once, credentials cached) - this script
#     doesn't handle auth itself.

param(
    [string]$ContainerName = "vivnest-agent",
    [string]$AppSettingsPath = "C:\vivnest-agent\appsettings.json"
)

$ErrorActionPreference = "Stop"

$Image = "vivnestagentacr.azurecr.io/vivnest-agent:latest"

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
