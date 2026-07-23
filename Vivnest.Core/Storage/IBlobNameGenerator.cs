using Vivnest.Core.Models;

namespace Vivnest.Core.Storage;

public interface IBlobNameGenerator
{
    string Generate(BlobNameContext context);
}