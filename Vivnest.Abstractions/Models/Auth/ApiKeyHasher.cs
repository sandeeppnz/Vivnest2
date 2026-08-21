using System.Security.Cryptography;
using System.Text;

namespace Vivnest.Abstractions.Models.Auth;

public static class ApiKeyHasher
{
    public static string Hash(string apiKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(bytes);
    }
}
