namespace Vivnest.Cloud.Auth;

public sealed record ApiKeyCreationResult(string KeyId, string ApiKey, DateTime CreatedUtc);
