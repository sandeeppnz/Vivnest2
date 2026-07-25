using Azure.Storage.Queues.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Reflection.Metadata;
using System.Text;
using Vivnest.Cloud.Interfaces;
using Vivnest.Core.Models;

namespace Vivnest.Cloud.Functions;

public class CameraCapturedFunction
{
    private readonly ILogger<CameraCapturedFunction> _logger;
    private readonly ICameraCapturedHandler _handler;

    public CameraCapturedFunction(ILogger<CameraCapturedFunction> logger, ICameraCapturedHandler handler)
    {
        _logger = logger;
        _handler = handler;
    }

    [Function(nameof(CameraCapturedFunction))]
    public async Task Run(
       [QueueTrigger("camera-captured")]
            CameraCapturedMessage message,
       CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Raw message: {Message}",message);

            await _handler.HandleAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed processing {PartitionKey}/{RowKey}",
                message.PartitionKey,
                message.RowKey);

            throw; // Important: rethrow so Azure Functions retries
        }
    }

}