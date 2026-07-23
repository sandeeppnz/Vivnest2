using Vivnest.Core.Models;
using Vivnest.Core.Storage;

namespace Vivnest.Infrastructure.Storage;

public class BlobNameGenerator : IBlobNameGenerator
{
    public string Generate(BlobNameContext context)
    {
        return
            $"{context.CameraId}/" +
            $"{context.CapturedAt:yyyy/MM/dd/HH-mm-ss}" +
            $"{context.Extension}";
    }
}