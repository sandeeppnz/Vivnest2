using System.Text.Json;
using Azure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Constants;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;

using Vivnest.Abstraction.Agent.Commands;

namespace Vivnest.Agent.Runtime.Commands;

// Decision-log.md ADR-080 - RefreshConfiguration and ApplyConfiguration
// resolve to the exact same Agent-side mechanism once Cloud has already
// normalized both commands' Payload down to one shape
// (AgentConfigCommandPayload{TargetVersion}, see CommandDispatcher):
// compare TargetVersion against what's currently loaded, confirm the
// target version blob is real, and either report Succeeded immediately
// (already there) or signal a restart (different). Scoped to the Agent's
// own configuration only this pass - see the ADR for why device-level
// support (ApplyConfiguration's TargetDeviceId) is deferred. A shared
// base class rather than duplicated logic in two classes, since the two
// command types are genuinely identical here - they only differ in which
// CommandType string they're registered under.
public abstract class ConfigVersionCommandHandlerBase : ICommandHandler
{
    private readonly AzureBlobStorageClient _blobClient;
    private readonly AgentOptions _agentOptions;
    private readonly AgentConfigMetadataOptions _configMetadata;
    private readonly ILogger _logger;

    protected ConfigVersionCommandHandlerBase(
        AzureBlobStorageClient blobClient,
        IOptions<AgentOptions> agentOptions,
        IOptions<AgentConfigMetadataOptions> configMetadata,
        ILogger logger)
    {
        _blobClient = blobClient;
        _agentOptions = agentOptions.Value;
        _configMetadata = configMetadata.Value;
        _logger = logger;
    }

    public abstract string CommandType { get; }

    public async Task<CommandHandlerResult> HandleAsync(
        AgentCommandDetails command,
        CancellationToken cancellationToken)
    {
        AgentConfigCommandPayload? payload;

        try
        {
            payload = string.IsNullOrWhiteSpace(command.Payload)
                ? null
                : JsonSerializer.Deserialize<AgentConfigCommandPayload>(command.Payload);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload == null)
            return CommandHandlerResult.Failed("INVALID_PAYLOAD", "Command payload did not contain a TargetVersion.");

        var targetVersion = payload.TargetVersion;

        if (targetVersion == _configMetadata.ConfigurationVersion)
        {
            _logger.LogInformation(
                "{CommandType} command {CommandId}: already at configuration version {Version}, no restart needed.",
                command.CommandType, command.CommandId, targetVersion);

            return CommandHandlerResult.Succeeded($"Already at version {targetVersion}.");
        }

        // Versions published before tenant/site scoping were copied into
        // the scoped layout before the unscoped blobs were deleted
        // (ADR-091), so every adoptable version is addressable here and a
        // 404 means the version genuinely does not exist.
        var key = new ConfigBlobKey(
            _agentOptions.TenantId, _agentOptions.SiteId, _agentOptions.AgentId);

        try
        {
            await _blobClient.DownloadAsync(
                AgentConfigBlob.ContainerName,
                AgentConfigBlob.VersionBlobName(key, targetVersion),
                cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return CommandHandlerResult.Failed(
                "VERSION_NOT_FOUND", $"Configuration version {targetVersion} blob was not found.");
        }

        _logger.LogInformation(
            "{CommandType} command {CommandId}: restarting to adopt configuration version {Version} (currently {CurrentVersion}).",
            command.CommandType, command.CommandId, targetVersion, _configMetadata.ConfigurationVersion);

        return CommandHandlerResult.Restart();
    }
}
