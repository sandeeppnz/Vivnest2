using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vivnest.Core.Constants;
using Vivnest.Core.Options;
using Vivnest.Core.Storage;
using Vivnest.Infrastructure.Azure;

namespace Vivnest.Agent.Commands;

public sealed class ApplyConfigurationCommandHandler : ConfigVersionCommandHandlerBase
{
    public ApplyConfigurationCommandHandler(
        AzureBlobStorageClient blobClient,
        IOptions<AgentOptions> agentOptions,
        IOptions<AgentConfigMetadataOptions> configMetadata,
        ILogger<ApplyConfigurationCommandHandler> logger)
        : base(blobClient, agentOptions, configMetadata, logger)
    {
    }

    public override string CommandType => AgentCommandTypes.ApplyConfiguration;
}
