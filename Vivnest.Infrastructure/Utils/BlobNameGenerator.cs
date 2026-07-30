using Vivnest.Core.Utils;

namespace Vivnest.Infrastructure.Utils;

public class BlobNameGenerator : IBlobNameGenerator
{
    public string Generate(BlobNameContext context)
    {
        return
            $"{context.TenantId}/" +
            $"{context.SiteId}/" +
            $"{context.AgentId}/" +
            $"{context.CameraId}/" +
            $"{context.CapturedAt:yyyy/MM/dd/HH-mm-ss}" +
            $"{context.Extension}";
    }
}
