using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Constants;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;

namespace Vivnest.Agent.Runtime.Shell;

// Own BackgroundService, own timer, own try/catch - same reasoning as
// AgentMetricsWorker: a log-shipping hiccup must never be able to touch
// anything else, least of all the logging it's trying to ship. Each tick
// overwrites the blob with the buffer's current snapshot rather than
// appending, so there's no "clear after flush" step to get wrong.
public sealed class LogShippingWorker : BackgroundService
{
    private readonly IAgentLogBuffer _buffer;
    private readonly AzureBlobStorageClient _blobStorage;
    private readonly AgentOptions _agentOptions;
    private readonly AgentLogShippingOptions _options;
    private readonly ILogger<LogShippingWorker> _logger;

    public LogShippingWorker(
        IAgentLogBuffer buffer,
        AzureBlobStorageClient blobStorage,
        IOptions<AgentOptions> agentOptions,
        IOptions<AgentLogShippingOptions> options,
        ILogger<LogShippingWorker> logger)
    {
        _buffer = buffer;
        _blobStorage = blobStorage;
        _agentOptions = agentOptions.Value;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Log shipping disabled.");
            return;
        }

        using var timer = new PeriodicTimer(_options.FlushInterval);

        do
        {
            try
            {
                await FlushAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ship agent logs.");
            }

        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        var lines = _buffer.Snapshot();

        if (lines.Count == 0)
            return;

        var content = string.Join(Environment.NewLine, lines);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        await _blobStorage.UploadAsync(
            AgentLogBlob.ContainerName,
            AgentLogBlob.BlobName(_agentOptions.AgentId),
            stream,
            cancellationToken);
    }
}
