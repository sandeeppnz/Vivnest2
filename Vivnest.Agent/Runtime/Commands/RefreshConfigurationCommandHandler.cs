using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Constants;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.Azure;

namespace Vivnest.Agent.Runtime.Commands;

public sealed class RefreshConfigurationCommandHandler : ConfigVersionCommandHandlerBase
{
    public RefreshConfigurationCommandHandler(
        AzureBlobStorageClient blobClient,
        IOptions<AgentOptions> agentOptions,
        IOptions<AgentConfigMetadataOptions> configMetadata,
        ILogger<RefreshConfigurationCommandHandler> logger)
        : base(blobClient, agentOptions, configMetadata, logger)
    {
    }

    public override string CommandType => AgentCommandTypes.RefreshConfiguration;
}
