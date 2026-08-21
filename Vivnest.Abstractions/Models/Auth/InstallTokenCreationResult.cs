namespace Vivnest.Abstractions.Models.Auth;

public sealed record InstallTokenCreationResult(string InstallToken, DateTime ExpiresUtc);
