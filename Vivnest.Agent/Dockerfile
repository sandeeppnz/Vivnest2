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

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

# RtspCamera resolves "ffmpeg" off PATH on non-Windows - see RtspCamera.cs.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# appsettings.json ships with the image for structure/defaults only - real
# per-machine config (camera credentials, storage connection string, which
# devices this Agent owns) should override it via environment variables
# (Storage__ConnectionString, Agent__TenantId, etc.) or a mounted
# appsettings.Production.json, not be baked into the image.
ENTRYPOINT ["dotnet", "Vivnest.Agent.dll"]
