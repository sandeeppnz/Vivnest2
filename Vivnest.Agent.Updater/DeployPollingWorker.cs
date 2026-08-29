using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Options;
using Vivnest.Core.Queues.Models;
using AzureQueueMessage = Azure.Storage.Queues.Models.QueueMessage;
using Vivnest.Agent.Updater.Configuration;

namespace Vivnest.Agent.Updater;

// Runs as its own standalone process directly on the host, never inside
// Vivnest.Agent's container - it needs the Docker access the Agent
// container is deliberately refused (ADR-020). Mirrors
// CommandPollingWorker's polling shape almost exactly (poll, delete
// before processing, per-tick error isolation), just consuming a
// different queue and acting on the host instead of the process itself.
// See ADR-028. The actual docker pull/stop/rm/run sequence lives in
// AgentDeployer, shared with the --install CLI flag (Program.cs).
public sealed class DeployPollingWorker : BackgroundService
{
    private readonly QueueServiceClient _queueServiceClient;
    private readonly AgentOptions _agentOptions;
    private readonly MessagingOptions _messagingOptions;
    private readonly DeployOptions _deployOptions;
    private readonly AgentDeployer _deployer;
    private readonly ILogger<DeployPollingWorker> _logger;

    public DeployPollingWorker(
        QueueServiceClient queueServiceClient,
        IOptions<AgentOptions> agentOptions,
        IOptions<MessagingOptions> messagingOptions,
        IOptions<DeployOptions> deployOptions,
        AgentDeployer deployer,
        ILogger<DeployPollingWorker> logger)
    {
        _queueServiceClient = queueServiceClient;
        _agentOptions = agentOptions.Value;
        _messagingOptions = messagingOptions.Value;
        _deployOptions = deployOptions.Value;
        _deployer = deployer;
        _logger = logger;
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
            _deployOptions.EffectivePollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await queue.ReceiveMessagesAsync(
                    maxMessages: 10,
                    visibilityTimeout: TimeSpan.FromSeconds(10),
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

            await Task.Delay(_deployOptions.EffectivePollInterval, stoppingToken);
        }
    }

    private async Task HandleMessageAsync(
        QueueClient queue,
        AzureQueueMessage message,
        CancellationToken cancellationToken)
    {
        // Parse before delete (ADR-123, same fix as the Agent's
        // QueuePollingWorkerBase): two Updater instances on one host - the
        // exact scenario DeployOptions.ContainerName exists for - share
        // this queue, so a message addressed to the other instance's agent
        // is LEFT for it, not consumed. Delete-first still holds for this
        // instance's own messages and unparseable ones: losing a deploy
        // request to a rare transient error just means clicking Deploy
        // again; a malformed message crash-looping this process forever is
        // worse.
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

            await queue.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            return;
        }

        if (command is null)
        {
            _logger.LogWarning(
                "Deploy command message {MessageId} deserialized to null; discarding.",
                message.MessageId);

            await queue.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
            return;
        }

        if (!string.Equals(command.AgentId, _agentOptions.AgentId, StringComparison.Ordinal))
        {
            if (message.DequeueCount >= 100)
            {
                _logger.LogWarning(
                    "Deploy command addressed to {TargetAgentId} has bounced {DequeueCount} times unclaimed; discarding.",
                    command.AgentId,
                    message.DequeueCount);

                await queue.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
                return;
            }

            _logger.LogDebug(
                "Deploy command addressed to {TargetAgentId}, not this agent ({AgentId}); leaving it in the queue.",
                command.AgentId,
                _agentOptions.AgentId);

            return;
        }

        await queue.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);

        _logger.LogInformation(
            "Deploy command received (issued {IssuedAtUtc}); pulling {ImageVersion} and recreating {ContainerName}.",
            command.IssuedAtUtc,
            command.ImageVersion ?? "latest",
            _deployOptions.ContainerName);

        await _deployer.DeployAsync(cancellationToken, command.ImageVersion);
    }
}
