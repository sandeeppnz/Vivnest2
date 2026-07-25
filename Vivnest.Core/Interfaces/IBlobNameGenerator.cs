using Vivnest.Core.Models;

namespace Vivnest.Core.Interfaces;

public interface IBlobNameGenerator
{
    string Generate(BlobNameContext context);
}
