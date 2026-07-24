
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




Vivnest MVP Roadmap
Phase 1 - Agent (✅ Almost Complete)

Responsible for producing data.

Capture Worker
Heartbeat Worker
Blob Storage
Heartbeat Table

Output:

Images
Heartbeats
Phase 2 - Notification Engine

Responsible for consuming data and notifying users.

                  Blob
                    │
                    │
Heartbeat           │
     │              │
     └──────┬───────┘
            │
      Notification Engine
            │
     ┌──────┼──────────────┐
     │      │              │
   Email Telegram WhatsApp Messenger

Notice every channel receives the same notification.

Notification Types

Then define notification types.

1. Daily Summary ⭐⭐⭐⭐⭐

Example

Vivnest Daily Summary

Home Agent

Status
✓ Healthy

Captures
24/24

Latest Capture
09:00

Failures
0

Attached
- latest.jpg

Delivery

Email
Telegram
WhatsApp
Messenger
2. Instant Alert ⭐⭐⭐⭐⭐
Camera has not captured
for 70 minutes.

Last capture

09:00

Error

Authentication failed

Delivery

Telegram
WhatsApp
Email
3. Scheduled Snapshot ⭐⭐⭐⭐☆

This is what you mentioned.

Every hour

Living Room

(photo)

or

Every 30 minutes

Front Door

(photo)

This isn't really an alert.

It's just a scheduled notification.

4. Daily Album ⭐⭐⭐⭐☆

Exactly what you suggested.

Instead of

1 image

send

Morning

(photo)

Afternoon

(photo)

Evening

(photo)

Night

(photo)

or

24 images

attached as a zip.

5. Motion Alert (Future)
Motion detected

(photo)
6. Camera Offline
Heartbeat lost

Agent offline

Last seen

08:43
Don't make Email special

This is the important design decision.

Instead of

DailyEmailWorker

I would build

NotificationWorker

Then define channels.

INotificationChannel

Email

Telegram

Messenger

WhatsApp

Each channel simply implements

SendAsync(Notification notification)

Then your worker says

NotificationWorker

↓

Build Daily Summary

↓

foreach channel

Send()
Notification Model

Something like

Notification

Type

Title

Message

Images

Priority

OccurredAt

Every channel receives the same object.

Configuration
Notifications

    DailySummary

        Enabled

        Time

        Channels

            Email

            Telegram

    Alerts

        Enabled

        Channels

            Telegram

            WhatsApp

    ScheduledSnapshots

        Enabled

        Interval

        Channels

            Telegram

Notice you're configuring features, not platforms.

This scales beautifully

Later

Discord

Slack

Signal

Push Notifications

Teams

become

INotificationChannel

Nothing else changes.

I think this should become the next major milestone

I'd call it something like:

Phase 2 – Notification & Reporting

Under that umbrella you can implement features incrementally:

Notification model (Notification, INotificationChannel).
Email channel (the easiest and most universally useful).
Telegram channel (great for photos and quick testing).
Daily summary notification.
Health alerts (capture overdue, heartbeat lost).
Scheduled snapshots (hourly or custom interval).
Daily photo album (selected images or a ZIP of captures).

This approach keeps reporting and alerts under a single, cohesive subsystem rather than treating email, Telegram, WhatsApp, and Messenger as separate projects. For Vivnest, that will give you a cleaner architecture and make it much easier to add new delivery channels over time.


License
- See repository for license details (if present).

