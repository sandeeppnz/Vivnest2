using System.Diagnostics;
using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Options;
using Vivnest.Core.Queues.Models;
using AzureQueueMessage = Azure.Storage.Queues.Models.QueueMessage;

namespace Vivnest.Agent.Updater;

// Runs as its own standalone process directly on the host, never inside
// Vivnest.Agent's container - it needs the Docker access the Agent
// container is deliberately refused (ADR-020). Mirrors
// CommandPollingWorker's polling shape almost exactly (poll, delete
// before processing, per-tick error isolation), just consuming a
// different queue and acting on the host instead of the process itself.
// See ADR-028.
public sealed class DeployPollingWorker : BackgroundService
{
    // Hardcoded, matching scripts/update-agent.ps1 exactly - the same
    // manual redeploy this automates. Not config-driven yet; a real second
    // agent/image would be the trigger to make these configurable.
    private const string Image = "vivnestagentacr.azurecr.io/vivnest-agent:latest";
    private const string ContainerName = "vivnest-agent";

    private readonly QueueServiceClient _queueServiceClient;
    private readonly AgentOptions _agentOptions;
    private readonly MessagingOptions _messagingOptions;
    private readonly DeployOptions _deployOptions;
    private readonly ILogger<DeployPollingWorker> _logger;
    private readonly string _appSettingsPath;

    public DeployPollingWorker(
        QueueServiceClient queueServiceClient,
        IOptions<AgentOptions> agentOptions,
        IOptions<MessagingOptions> messagingOptions,
        IOptions<DeployOptions> deployOptions,
        ILogger<DeployPollingWorker> logger)
    {
        _queueServiceClient = queueServiceClient;
        _agentOptions = agentOptions.Value;
        _messagingOptions = messagingOptions.Value;
        _deployOptions = deployOptions.Value;
        _logger = logger;

        // The same folder this executable is deployed into, not a
        // hardcoded C:\vivnest-agent path - so the exact same build works
        // wherever it's dropped (Windows today, a Raspberry Pi later),
        // since appsettings.json always sits right next to it.
        _appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_messagingOptions.DeployCommandQueue))
        {
            _logger.LogWarning(
                "Messaging:DeployCommandQueue not configured; Deploy Polling Worker has nothing to poll.");

            return;
        }

        var queue = _queueServiceClient.GetQueueClient(_messagingOptions.DeployCommandQueue);

        await queue.CreateIfNotExistsAsync(cancellationToken: stoppingToken);

        _logger.LogInformation(
            "Deploy Polling Worker started, polling {Queue} every {Interval}.",
            _messagingOptions.DeployCommandQueue,
            _deployOptions.PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await queue.ReceiveMessagesAsync(
                    maxMessages: 10,
                    cancellationToken: stoppingToken);

                foreach (var message in response.Value)
                {
                    await HandleMessageAsync(queue, message, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deploy Polling Worker tick failed.");
            }

            await Task.Delay(_deployOptions.PollInterval, stoppingToken);
        }
    }

    private async Task HandleMessageAsync(
        QueueClient queue,
        AzureQueueMessage message,
        CancellationToken cancellationToken)
    {
        // Delete first, not after processing - same non-retrying design as
        // CommandPollingWorker. Losing a deploy request to a rare transient
        // error just means clicking Deploy again; a malformed message
        // crash-looping this process forever is worse.
        await queue.DeleteMessageAsync(
            message.MessageId,
            message.PopReceipt,
            cancellationToken);

        DeployCommandQueueMessage? command;

        try
        {
            command = JsonSerializer.Deserialize<DeployCommandQueueMessage>(message.MessageText);
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Unable to deserialize deploy command message {MessageId}; discarding.",
                message.MessageId);

            return;
        }

        if (command is null)
        {
            _logger.LogWarning(
                "Deploy command message {MessageId} deserialized to null; discarding.",
                message.MessageId);

            return;
        }

        if (!string.Equals(command.AgentId, _agentOptions.AgentId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Deploy command addressed to {TargetAgentId}, not this agent ({AgentId}); discarding.",
                command.AgentId,
                _agentOptions.AgentId);

            return;
        }

        _logger.LogInformation(
            "Deploy command received (issued {IssuedAtUtc}); pulling latest image and recreating {ContainerName}.",
            command.IssuedAtUtc,
            ContainerName);

        await DeployAsync(cancellationToken);
    }

    private async Task DeployAsync(CancellationToken cancellationToken)
    {
        await RunDockerAsync(cancellationToken, allowFailure: false, "pull", Image);
        await RunDockerAsync(cancellationToken, allowFailure: true, "stop", ContainerName);
        await RunDockerAsync(cancellationToken, allowFailure: true, "rm", ContainerName);

        // Same flags as scripts/update-agent.ps1 - keep both in sync if
        // the container's run configuration ever changes.
        await RunDockerAsync(
            cancellationToken,
            allowFailure: false,
            "run", "-d",
            "--name", ContainerName,
            "--restart", "unless-stopped",
            "-v", $"{_appSettingsPath}:/app/appsettings.json",
            "-e", "HomeAssistant__BaseUrl=http://host.docker.internal:8123/",
            Image);

        _logger.LogInformation(
            "Deploy complete: {ContainerName} recreated from {Image}.",
            ContainerName,
            Image);
    }

    private async Task RunDockerAsync(
        CancellationToken cancellationToken,
        bool allowFailure,
        params string[] arguments)
    {
        _logger.LogInformation("Running: docker {Arguments}", string.Join(' ', arguments));

        var startInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start docker process.");

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        if (!string.IsNullOrWhiteSpace(stdOut))
            _logger.LogInformation("{Output}", stdOut.Trim());

        if (!string.IsNullOrWhiteSpace(stdErr))
            _logger.LogInformation("{Output}", stdErr.Trim());

        if (process.ExitCode != 0 && !allowFailure)
        {
            throw new InvalidOperationException(
                $"docker {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.");
        }
    }
}
