namespace Vivnest.Abstractions.Models.Auth;

public sealed record ApiKeyCreationResult(string KeyId, string ApiKey, DateTime CreatedUtc);
