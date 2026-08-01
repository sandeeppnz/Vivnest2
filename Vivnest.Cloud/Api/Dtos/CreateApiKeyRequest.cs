namespace Vivnest.Cloud.Api.Dtos;

public sealed record CreateApiKeyRequest(string TenantId, string SiteId, string? Name);
