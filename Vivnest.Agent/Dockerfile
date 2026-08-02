# Build context must be the repo root (multi-project solution):
#   docker build -t vivnest-agent:latest -f Vivnest.Agent/Dockerfile .

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

ENTRYPOINT ["dotnet", "Vivnest.Agent.dll"]
