using Microsoft.Extensions.Logging;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Queues.Models;

namespace Vivnest.Cloud.Handlers;

// Pure relay, unlike every other queue handler in this codebase - the
// incoming message already carries its complete payload (ADR-004
// exception, same as RestartCommandQueueMessage), so there is no table
// row to re-fetch and no MarkCompleted/MarkFailed state to update. This
// hop only re-addresses the message from the Low-type agent's outbound
// queue to the High-type agent's inbound one - see decision-log.md ADR-035.
public sealed class ClassifyRequestHandler : IClassifyRequestHandler
{
    private readonly IAgentCommandPublisher _commandPublisher;
    private readonly ILogger<ClassifyRequestHandler> _logger;

    public ClassifyRequestHandler(
        IAgentCommandPublisher commandPublisher,
        ILogger<ClassifyRequestHandler> logger)
    {
        _commandPublisher = commandPublisher;
        _logger = logger;
    }

    public async Task HandleAsync(
        ClassifyCaptureQueueMessage message,
        CancellationToken cancellationToken = default)
    {
        await _commandPublisher.PublishClassifyCommandAsync(message, cancellationToken);

        _logger.LogInformation(
            "Relayed classify request for {DeviceId} to High-type agent {AgentId}",
            message.DeviceId,
            message.AgentId);
    }
}
