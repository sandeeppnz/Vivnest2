# Build context must be the repo root (multi-project solution):
#   docker build --build-arg BUILD_VERSION=$(git rev-parse --short HEAD) \
#     -t vivnest-agent:latest -f Vivnest.Agent/Dockerfile .
# BUILD_VERSION is optional (defaults to "unknown") but should always be
# passed for a real deployment - see the ARG/ENV near the bottom of this
# file for why.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Vivnest.Core/Vivnest.Core.csproj Vivnest.Core/
COPY Vivnest.Infrastructure/Vivnest.Infrastructure.csproj Vivnest.Infrastructure/
COPY Vivnest.Agent/Vivnest.Agent.csproj Vivnest.Agent/
RUN dotnet restore Vivnest.Agent/Vivnest.Agent.csproj

COPY Vivnest.Core/ Vivnest.Core/
COPY Vivnest.Infrastructure/ Vivnest.Infrastructure/
COPY Vivnest.Agent/ Vivnest.Agent/

RUN dotnet publish Vivnest.Agent/Vivnest.Agent.csproj -c Release -o /app/publish --no-restore

# appsettings.json carries real per-deployment secrets (storage connection
# string, camera credentials) - dotnet publish copies it in for local dev's
# sake (see Vivnest.Agent.csproj), but it must not ship inside the image.
# Config comes from environment variables (already supported for free - the
# Generic Host includes AddEnvironmentVariables() by default, same
# Section__Key convention Cloud.Functions' app settings already use) or a
# file mounted at /app/appsettings.json at `docker run` time instead.
RUN rm -f /app/publish/appsettings.json

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

# RtspCamera resolves "ffmpeg" off PATH on non-Windows - see RtspCamera.cs.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Baked in at build time, not typed into per-deployment config - a manually
# maintained version string can't be trusted to reflect what's actually
# running (see decision-log.md). Pass the real commit at build time:
#   docker build --build-arg BUILD_VERSION=$(git rev-parse --short HEAD) ...
# Falls through the same Agent:FirmwareVersion config key
# AgentHeartbeatWorker already reads (AddEnvironmentVariables()'s
# Section__Key convention), so no C# changes were needed for this. A
# `docker run -e Agent__FirmwareVersion=...` would still override this,
# same as any other env var - nothing does that today.
ARG BUILD_VERSION=unknown
ENV Agent__FirmwareVersion=$BUILD_VERSION

ENTRYPOINT ["dotnet", "Vivnest.Agent.dll"]
