using Vivnest.Core.Domain;

namespace Vivnest.Core.Utils;

public interface IBlobNameGenerator
{
    string Generate(BlobNameContext context);
}
