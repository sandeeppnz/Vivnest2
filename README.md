
# Vivnest

Vivnest is a small .NET agent for capturing photos from cameras and storing them in a backing storage. The solution is split into three projects:

- Vivnest.Agent - the executable worker/host that runs capture and heartbeat workers.
- Vivnest.Core - core interfaces, models and option types used across the solution.
- Vivnest.Infrastructure - concrete implementations (camera drivers, storage providers, heartbeat persistence).

Prerequisites
- .NET 10 SDK

Build
- From the repository root run: dotnet build

Run
- Run the agent from the repository root: dotnet run --project Vivnest.Agent

Configuration
The agent uses configuration bound to option classes in Vivnest.Core. Typical configuration sections include:

- Agent
- Camera
- Storage
- Heartbeat


Example appsettings.json (adjust to your environment):

```
{
  "Agent": {
    "Enabled": true
  },
  "Camera": {
    "Type": "Rtsp",
    "RtspUrl": "<RTSP_URL>",
    "Username": "<RTSP_USERNAME>",
    "Password": "<RTSP_PASSWORD>"
  },
  "Storage": {
    "Type": "AzureBlob",
    "ConnectionString": "<STORAGE_CONNECTION_STRING>",
    "Container": "photos"
  },
  "Heartbeat": {
    "IntervalSeconds": 60,
    "TableName": "Heartbeats"
  }
}
```

Replace the placeholder values with your configuration values. Do not store real secrets in the repository. See Secrets and best practices below.

- Testing

Secrets and best practices
- Never commit real secrets (connection strings, API keys, passwords) to source control.
- For local development, use dotnet user-secrets or environment variables to store secrets.
- In production, use a secrets manager such as Azure Key Vault or another secure store and inject secrets at runtime via environment variables or a managed identity.
- If a secret was committed accidentally, rotate the secret and remove it from the Git history using tools like git filter-repo or BFG Repo-Cleaner.

Run unit tests: dotnet test Vivnest.Agent.Tests

Contributing
- Fork, create a feature branch, add tests for changes and open a pull request.

License
- See repository for license details (if present).

